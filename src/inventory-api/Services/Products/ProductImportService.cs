using System;
using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Infrastructure.Logging;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Services.Products.Import;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportService : IProductImportService
{
    private const long AdvisoryLockKey = 297351;

    private readonly IDbConnection _connection;
    private readonly IClock _clock;
    private readonly ILogger<ProductImportService> _logger;
    private readonly IProductImportMetrics _metrics;
    private readonly IProductImportReader _reader;
    private readonly IProductImportValidator _validator;
    private readonly IProductImportWriter _writer;
    private readonly IProductImportHistoryStore _historyStore;

    public ProductImportService(
        IDbConnection connection,
        IClock clock,
        ILogger<ProductImportService> logger,
        IProductImportMetrics metrics,
        IProductImportReader reader,
        IProductImportValidator validator,
        IProductImportWriter writer,
        IProductImportHistoryStore historyStore)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
    }

    public async Task<ProductImportResult> ImportAsync(ProductImportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.CsvStream);

        if (command.ShopId == Guid.Empty)
        {
            throw new ArgumentException("Shop identifier is required for product imports.", nameof(command));
        }

        using var buffer = await _reader.BufferAsync(command.CsvStream, cancellationToken).ConfigureAwait(false);

        if (_connection is not NpgsqlConnection npgsqlConnection)
        {
            throw new InvalidOperationException("L'import produit requiert une connexion Npgsql active.");
        }

        await EnsureConnectionOpenAsync(npgsqlConnection, cancellationToken).ConfigureAwait(false);

        await using var transaction = await npgsqlConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var lockAcquired = await TryAcquireAdvisoryLockAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!lockAcquired)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new ProductImportInProgressException();
        }

        var shopId = command.ShopId;

        try
        {
            var historyId = Guid.NewGuid();
            var startedAt = _clock.UtcNow;

            await _historyStore.InsertStartedAsync(
                    historyId,
                    shopId,
                    startedAt,
                    command.Username,
                    buffer.Sha256,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.SaveAsync("before_import", cancellationToken).ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();
            _metrics.IncrementStarted();

            ProductCsvParseOutcome parseOutcome;
            if (buffer.Stream.Length == 0)
            {
                parseOutcome = ProductCsvParseOutcome.CreateEmptyFile();
            }
            else
            {
                parseOutcome = await _reader.ParseAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            var totalLines = parseOutcome.TotalLines;
            var unknownColumns = parseOutcome.UnknownColumns;

            LogImportEvent(
                "ImportStarted",
                ProductImportHistoryStatuses.Started,
                command.Username,
                buffer.Sha256,
                totalLines,
                created: 0,
                updated: 0,
                TimeSpan.Zero,
                unknownColumns);

            var validation = _validator.Validate(parseOutcome);
            if (!validation.IsValid)
            {
                var finishedAt = _clock.UtcNow;

                await _historyStore.CompleteAsync(
                        historyId,
                        ProductImportHistoryStatuses.Failed,
                        totalLines,
                        inserted: 0,
                        parseOutcome.Errors.Count,
                        finishedAt,
                        stopwatch.Elapsed,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                _metrics.IncrementFailed();
                _metrics.ObserveDuration(stopwatch.Elapsed, command.DryRun);

                LogImportEvent(
                    "ImportCompleted",
                    ProductImportHistoryStatuses.Failed,
                    command.Username,
                    buffer.Sha256,
                    totalLines,
                    created: 0,
                    updated: 0,
                    stopwatch.Elapsed,
                    unknownColumns);

                return new ProductImportResult(
                    ProductImportResponse.Failure(
                        totalLines,
                        parseOutcome.Errors,
                        unknownColumns,
                        parseOutcome.ProposedGroups,
                        parseOutcome.SkippedLines,
                        parseOutcome.Duplicates),
                    ProductImportResultType.ValidationFailed);
            }

            if (command.DryRun)
            {
                var preview = await _writer
                    .PreviewAsync(parseOutcome.Rows, shopId, transaction, cancellationToken)
                    .ConfigureAwait(false);

                var finishedAt = _clock.UtcNow;

                await _historyStore.CompleteAsync(
                        historyId,
                        ProductImportHistoryStatuses.DryRun,
                        totalLines,
                        inserted: 0,
                        errorCount: 0,
                        finishedAt,
                        stopwatch.Elapsed,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                _metrics.IncrementSucceeded(true);
                _metrics.ObserveDuration(stopwatch.Elapsed, true);

                LogImportEvent(
                    "ImportCompleted",
                    ProductImportHistoryStatuses.DryRun,
                    command.Username,
                    buffer.Sha256,
                    totalLines,
                    created: preview.Created,
                    updated: preview.Updated,
                    stopwatch.Elapsed,
                    unknownColumns);

                return new ProductImportResult(
                    ProductImportResponse.DryRunResult(
                        totalLines,
                        preview.Created,
                        preview.Updated,
                        unknownColumns,
                        parseOutcome.ProposedGroups,
                        parseOutcome.SkippedLines,
                        parseOutcome.Duplicates),
                    ProductImportResultType.DryRun);
            }

            try
            {
                var writeStats = await _writer
                    .WriteAsync(parseOutcome.Rows, shopId, command.Mode, transaction, cancellationToken)
                    .ConfigureAwait(false);

                var finishedAt = _clock.UtcNow;

                await _historyStore.CompleteAsync(
                        historyId,
                        ProductImportHistoryStatuses.Succeeded,
                        totalLines,
                        inserted: writeStats.Created + writeStats.Updated,
                        errorCount: 0,
                        finishedAt,
                        stopwatch.Elapsed,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                _metrics.IncrementSucceeded(false);
                _metrics.ObserveDuration(stopwatch.Elapsed, false);

                LogImportEvent(
                    "ImportCompleted",
                    ProductImportHistoryStatuses.Succeeded,
                    command.Username,
                    buffer.Sha256,
                    totalLines,
                    created: writeStats.Created,
                    updated: writeStats.Updated,
                    stopwatch.Elapsed,
                    unknownColumns);

                ApiLog.ImportStep(
                    _logger,
                    $"Import produits terminé : {writeStats.Created} créations, {writeStats.Updated} mises à jour.");

                return new ProductImportResult(
                    ProductImportResponse.Success(
                        totalLines,
                        writeStats.Created,
                        writeStats.Updated,
                        unknownColumns,
                        parseOutcome.ProposedGroups,
                        parseOutcome.SkippedLines,
                        parseOutcome.Duplicates),
                    ProductImportResultType.Succeeded);
            }
            catch
            {
                await transaction.RollbackAsync("before_import", cancellationToken).ConfigureAwait(false);

                var finishedAt = _clock.UtcNow;

                await _historyStore.CompleteAsync(
                        historyId,
                        ProductImportHistoryStatuses.Failed,
                        totalLines,
                        inserted: 0,
                        errorCount: 0,
                        finishedAt,
                        stopwatch.Elapsed,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                _metrics.IncrementFailed();
                _metrics.ObserveDuration(stopwatch.Elapsed, command.DryRun);

                LogImportEvent(
                    "ImportCompleted",
                    ProductImportHistoryStatuses.Failed,
                    command.Username,
                    buffer.Sha256,
                    totalLines,
                    created: 0,
                    updated: 0,
                    stopwatch.Elapsed,
                    unknownColumns);

                throw;
            }
        }
        finally
        {
            if (lockAcquired)
            {
                await ReleaseAdvisoryLockAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void LogImportEvent(
        string eventName,
        string status,
        string? username,
        string fileSha256,
        int total,
        int created,
        int updated,
        TimeSpan duration,
        System.Collections.Generic.IReadOnlyCollection<string>? unknownColumns)
    {
        var payload = new
        {
            Username = username,
            FileSha256 = fileSha256,
            Total = total,
            Created = created,
            Updated = updated,
            DurationMs = (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero),
            Status = status,
            UnknownColumns = unknownColumns ?? Array.Empty<string>()
        };

        ApiLog.ImportStep(_logger, $"{eventName} {JsonSerializer.Serialize(payload, ProductImportSerialization.Options)}");
    }

    private async Task<bool> TryAcquireAdvisoryLockAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_try_advisory_lock(@Key);";
        return await _connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { Key = AdvisoryLockKey }, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task ReleaseAdvisoryLockAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_advisory_unlock(@Key);";
        await _connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { Key = AdvisoryLockKey }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private static async Task EnsureConnectionOpenAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.FullState.HasFlag(ConnectionState.Open))
        {
            return;
        }

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }
}
