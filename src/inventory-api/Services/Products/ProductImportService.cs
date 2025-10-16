using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportService : IProductImportService
{
    private const string CsvHeaderBarcode = "barcode_rfid";
    private const string CsvHeaderItem = "item";
    private const string CsvHeaderDescription = "descr";
    private const long MaxCsvSizeBytes = 25L * 1024L * 1024L;
    private const long AdvisoryLockKey = 297351;
    private const string StatusStarted = "Started";
    private const string StatusSucceeded = "Succeeded";
    private const string StatusFailed = "Failed";
    private const string StatusDryRun = "DryRun";
    private const string StatusSkipped = "Skipped";

    private static readonly Regex DigitsOnlyRegex = new("[^0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IDbConnection _connection;
    private readonly IClock _clock;
    private readonly ILogger<ProductImportService> _logger;
    private readonly IProductImportMetrics _metrics;

    public ProductImportService(
        IDbConnection connection,
        IClock clock,
        ILogger<ProductImportService> logger,
        IProductImportMetrics metrics)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public async Task<ProductImportResult> ImportAsync(ProductImportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.CsvStream);

        var bufferedCsv = await BufferStreamAsync(command.CsvStream, cancellationToken).ConfigureAwait(false);
        await using var bufferedStream = bufferedCsv.Stream;

        var encoding = DetectEncoding(bufferedStream);

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

        try
        {
            if (!command.DryRun)
            {
                var lastSucceededHash = await GetLastSucceededHashAsync(transaction, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(lastSucceededHash) &&
                    string.Equals(lastSucceededHash, bufferedCsv.Sha256, StringComparison.Ordinal))
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    LogImportEvent(
                        "ImportCompleted",
                        StatusSkipped,
                        command.Username,
                        bufferedCsv.Sha256,
                        total: 0,
                        inserted: 0,
                        duration: TimeSpan.Zero);
                    _logger.LogInformation("Import produits ignoré : fichier identique au dernier import réussi.");
                    return new ProductImportResult(ProductImportResponse.SkippedResult(), ProductImportResultType.Skipped);
                }
            }

            var historyId = Guid.NewGuid();
            var startedAt = _clock.UtcNow;

            await InsertHistoryStartedAsync(historyId, startedAt, command.Username, bufferedCsv.Sha256, transaction, cancellationToken)
                .ConfigureAwait(false);

            transaction.Save("before_import");

            var stopwatch = Stopwatch.StartNew();
            _metrics.IncrementStarted();

            ProductCsvParseOutcome parseOutcome;
            if (bufferedStream.Length == 0)
            {
                parseOutcome = ProductCsvParseOutcome.CreateEmptyFile();
            }
            else
            {
                bufferedStream.Position = 0;
                parseOutcome = await ParseAsync(bufferedStream, encoding, cancellationToken).ConfigureAwait(false);
            }

            var totalLines = parseOutcome.TotalLines;
            var errorCount = parseOutcome.Errors.Count;
            var wouldInsert = parseOutcome.Rows.Count;

            LogImportEvent(
                "ImportStarted",
                StatusStarted,
                command.Username,
                bufferedCsv.Sha256,
                totalLines,
                inserted: 0,
                duration: TimeSpan.Zero);

            if (errorCount > 0)
            {
                await CompleteHistoryAsync(
                        historyId,
                        StatusFailed,
                        totalLines,
                        inserted: 0,
                        errorCount,
                        transaction,
                        cancellationToken,
                        stopwatch.Elapsed)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                _metrics.IncrementFailed();
                _metrics.ObserveDuration(stopwatch.Elapsed, command.DryRun);

                LogImportEvent(
                    "ImportCompleted",
                    StatusFailed,
                    command.Username,
                    bufferedCsv.Sha256,
                    totalLines,
                    inserted: 0,
                    stopwatch.Elapsed);

                return new ProductImportResult(
                    ProductImportResponse.Failure(totalLines, parseOutcome.Errors, wouldInsert),
                    ProductImportResultType.ValidationFailed);
            }

            if (command.DryRun)
            {
                await CompleteHistoryAsync(
                        historyId,
                        StatusDryRun,
                        totalLines,
                        inserted: 0,
                        errorCount: 0,
                        transaction,
                        cancellationToken,
                        stopwatch.Elapsed)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                _metrics.IncrementSucceeded(true);
                _metrics.ObserveDuration(stopwatch.Elapsed, true);

                LogImportEvent(
                    "ImportCompleted",
                    StatusDryRun,
                    command.Username,
                    bufferedCsv.Sha256,
                    totalLines,
                    inserted: 0,
                    stopwatch.Elapsed);

                return new ProductImportResult(
                    ProductImportResponse.DryRunResult(totalLines, wouldInsert),
                    ProductImportResultType.DryRun);
            }

            try
            {
                await TruncateProductsAsync(transaction, cancellationToken).ConfigureAwait(false);

                var inserted = await InsertRowsAsync(parseOutcome.Rows, transaction, cancellationToken).ConfigureAwait(false);

                await CompleteHistoryAsync(
                        historyId,
                        StatusSucceeded,
                        totalLines,
                        inserted,
                        errorCount: 0,
                        transaction,
                        cancellationToken,
                        stopwatch.Elapsed)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                _metrics.IncrementSucceeded(false);
                _metrics.ObserveDuration(stopwatch.Elapsed, false);

                LogImportEvent(
                    "ImportCompleted",
                    StatusSucceeded,
                    command.Username,
                    bufferedCsv.Sha256,
                    totalLines,
                    inserted,
                    stopwatch.Elapsed);

                _logger.LogInformation("Import produits terminé : {Inserted} lignes insérées.", inserted);

                return new ProductImportResult(
                    ProductImportResponse.Success(totalLines, inserted),
                    ProductImportResultType.Succeeded);
            }
            catch
            {
                transaction.Rollback("before_import");

                await CompleteHistoryAsync(
                        historyId,
                        StatusFailed,
                        totalLines,
                        inserted: 0,
                        errorCount: 0,
                        transaction,
                        cancellationToken,
                        stopwatch.Elapsed)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                _metrics.IncrementFailed();
                _metrics.ObserveDuration(stopwatch.Elapsed, command.DryRun);

                LogImportEvent(
                    "ImportCompleted",
                    StatusFailed,
                    command.Username,
                    bufferedCsv.Sha256,
                    totalLines,
                    inserted: 0,
                    stopwatch.Elapsed);

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
        int inserted,
        TimeSpan duration)
    {
        var payload = new
        {
            Username = username,
            FileSha256 = fileSha256,
            Total = total,
            Inserted = inserted,
            DurationMs = (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero),
            Status = status
        };

        _logger.LogInformation("{Event} {@Import}", eventName, payload);
    }

    private async Task<BufferedCsv> BufferStreamAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        var destination = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long totalBytes = 0;

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > MaxCsvSizeBytes)
                {
                    throw new ProductImportPayloadTooLargeException(MaxCsvSizeBytes);
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            destination.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        destination.Position = 0;
        var sha256 = Convert.ToHexString(hash.GetHashAndReset());
        return new BufferedCsv(destination, sha256);
    }

    private static Encoding DetectEncoding(MemoryStream stream)
    {
        if (!stream.TryGetBuffer(out var segment))
        {
            var snapshot = stream.ToArray();
            return DetectEncoding(snapshot.AsSpan());
        }

        return DetectEncoding(segment.AsSpan());
    }

    private static Encoding DetectEncoding(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        try
        {
            StrictUtf8.GetString(buffer);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1;
        }
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

    private async Task<string?> GetLastSucceededHashAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT \"FileSha256\" FROM \"ProductImportHistory\" " +
            "WHERE \"Status\" = @Status AND \"FileSha256\" IS NOT NULL " +
            "ORDER BY \"StartedAt\" DESC LIMIT 1;";

        return await _connection.QueryFirstOrDefaultAsync<string?>(
                new CommandDefinition(sql, new { Status = StatusSucceeded }, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task InsertHistoryStartedAsync(
        Guid historyId,
        DateTimeOffset startedAt,
        string? username,
        string fileSha256,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql =
            "INSERT INTO \"ProductImportHistory\" (\"Id\", \"StartedAt\", \"Username\", \"FileSha256\", \"TotalLines\", \"Inserted\", \"ErrorCount\", \"Status\") " +
            "VALUES (@Id, @StartedAt, @Username, @FileSha256, 0, 0, 0, @Status);";

        var parameters = new
        {
            Id = historyId,
            StartedAt = startedAt,
            Username = username,
            FileSha256 = fileSha256,
            Status = StatusStarted
        };

        await _connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task CompleteHistoryAsync(
        Guid historyId,
        string status,
        int totalLines,
        int inserted,
        int errorCount,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken,
        TimeSpan elapsed)
    {
        const string sql =
            "UPDATE \"ProductImportHistory\" SET " +
            "\"FinishedAt\" = @FinishedAt, " +
            "\"DurationMs\" = @DurationMs, " +
            "\"Status\" = @Status, " +
            "\"TotalLines\" = @TotalLines, " +
            "\"Inserted\" = @Inserted, " +
            "\"ErrorCount\" = @ErrorCount " +
            "WHERE \"Id\" = @Id;";

        var finishedAt = _clock.UtcNow;
        var durationMs = (int)Math.Min(int.MaxValue, Math.Round(elapsed.TotalMilliseconds));

        var parameters = new
        {
            Id = historyId,
            FinishedAt = finishedAt,
            DurationMs = durationMs,
            Status = status,
            TotalLines = totalLines,
            Inserted = inserted,
            ErrorCount = errorCount
        };

        await _connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task<int> InsertRowsAsync(IReadOnlyList<ProductCsvRow> rows, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        const string sql =
            "INSERT INTO \"Product\" (\"Sku\", \"Name\", \"Ean\", \"CodeDigits\", \"CreatedAtUtc\") " +
            "VALUES (@Sku, @Name, @Ean, @CodeDigits, @CreatedAtUtc);";

        var now = _clock.UtcNow;
        foreach (var row in rows)
        {
            var ean = NormalizeEan(row.Code);
            var parameters = new
            {
                row.Sku,
                row.Name,
                Ean = ean,
                row.CodeDigits,
                CreatedAtUtc = now
            };

            await _connection.ExecuteAsync(
                    new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        return rows.Count;
    }

    private string? NormalizeEan(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (code.Length <= 13)
        {
            return code;
        }

        _logger.LogDebug("Code importé trop long ({Length} > 13), colonne EAN laissée vide.", code.Length);
        return null;
    }

    private async Task TruncateProductsAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = "TRUNCATE TABLE \"Product\" RESTART IDENTITY CASCADE;";
        await _connection.ExecuteAsync(
                new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task<ProductCsvParseOutcome> ParseAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
    {
        var rows = new List<ProductCsvRow>();
        var errors = new List<ProductImportError>();
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lineNumber = 0;
        var headerProcessed = false;
        var totalLines = 0;

        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } rawLine)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lineNumber++;
            var line = rawLine.TrimEnd('\r');

            if (!headerProcessed)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var headerFields = ParseFields(line);
                headerProcessed = true;
                if (!IsValidHeader(headerFields))
                {
                    errors.Add(new ProductImportError(lineNumber, "INVALID_HEADER"));
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            totalLines++;

            var fields = ParseFields(line);
            if (fields.Count != 3)
            {
                errors.Add(new ProductImportError(lineNumber, "INVALID_COLUMN_COUNT"));
                continue;
            }

            var codeRaw = fields[0].Trim();
            var sku = fields[1].Trim();
            var name = fields[2].Trim();

            if (string.IsNullOrWhiteSpace(sku))
            {
                errors.Add(new ProductImportError(lineNumber, "EMPTY_SKU"));
                continue;
            }

            if (!seenSkus.Add(sku))
            {
                errors.Add(new ProductImportError(lineNumber, "DUP_SKU_IN_FILE"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ProductImportError(lineNumber, "EMPTY_NAME"));
                continue;
            }

            var code = string.IsNullOrWhiteSpace(codeRaw) ? null : codeRaw;
            var digits = DigitsOnlyRegex.Replace(code ?? string.Empty, string.Empty);
            var codeDigits = string.IsNullOrEmpty(digits) ? null : digits;

            rows.Add(new ProductCsvRow(sku, name, code, codeDigits));
        }

        if (!headerProcessed && errors.Count == 0)
        {
            errors.Add(new ProductImportError(0, "MISSING_HEADER"));
        }

        return new ProductCsvParseOutcome(rows, errors, totalLines);
    }

    private static List<string> ParseFields(string line)
    {
        var result = new List<string>();
        var builder = new StringBuilder(line.Length);
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (current == '\"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                {
                    builder.Append('\"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (current == ';' && !inQuotes)
            {
                result.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        result.Add(builder.ToString());
        return result;
    }

    private static bool IsValidHeader(IReadOnlyList<string> columns)
    {
        if (columns.Count < 3)
        {
            return false;
        }

        return string.Equals(columns[0].Trim(), CsvHeaderBarcode, StringComparison.OrdinalIgnoreCase)
               && string.Equals(columns[1].Trim(), CsvHeaderItem, StringComparison.OrdinalIgnoreCase)
               && string.Equals(columns[2].Trim(), CsvHeaderDescription, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EnsureConnectionOpenAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.FullState.HasFlag(ConnectionState.Open))
        {
            return;
        }

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record BufferedCsv(MemoryStream Stream, string Sha256);

    private sealed record ProductCsvRow(string Sku, string Name, string? Code, string? CodeDigits);

    private sealed record ProductCsvParseOutcome(IReadOnlyList<ProductCsvRow> Rows, IReadOnlyList<ProductImportError> Errors, int TotalLines)
    {
        public static ProductCsvParseOutcome CreateEmptyFile()
        {
            return new ProductCsvParseOutcome(
                Array.Empty<ProductCsvRow>(),
                new[] { new ProductImportError(0, "EMPTY_FILE") },
                0);
        }
    }
}
