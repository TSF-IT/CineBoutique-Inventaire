using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CineBoutique.Inventory.Infrastructure.Database.Products;
using NpgsqlTypes;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportService : IProductImportService
{
    private const long MaxCsvSizeBytes = 25L * 1024L * 1024L;
    private const long GlobalAdvisoryLockKey = 297351;
    private const string StatusStarted = "Started";
    private const string StatusSucceeded = "Succeeded";
    private const string StatusFailed = "Failed";
    private const string StatusDryRun = "DryRun";
    private const string StatusSkipped = "Skipped";
    private const string ProductImportTable = "ProductImport";
    private const string ProductImportShopIdColumn = "ShopId";
    private const string ProductImportFileHashColumn = "FileHashSha256";
    private const string ProductImportRowCountColumn = "RowCount";

    private static readonly Regex DigitsOnlyRegex = new("[^0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
    private static readonly ImmutableArray<string> EmptyUnknownColumns = ImmutableArray<string>.Empty;
    private static readonly ImmutableArray<ProductImportGroupProposal> EmptyProposedGroups = ImmutableArray<ProductImportGroupProposal>.Empty;

    // Map d'équivalences d'entêtes → canonique (insensible à la casse)
    private static readonly Dictionary<string, string> HeaderSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sku"] = KnownColumns.Sku,
        ["item"] = KnownColumns.Sku,
        ["code"] = KnownColumns.Sku,
        ["ean"] = KnownColumns.Ean,
        ["ean13"] = KnownColumns.Ean,
        ["barcode"] = KnownColumns.Ean,
        ["barcode_rfid"] = KnownColumns.Ean,
        ["name"] = KnownColumns.Name,
        ["descr"] = KnownColumns.Description,
        ["description"] = KnownColumns.Description,
        ["libelle"] = KnownColumns.Name,
        ["groupe"] = KnownColumns.Group,
        ["group"] = KnownColumns.Group,
        ["sous_groupe"] = KnownColumns.SubGroup,
        ["subgroup"] = KnownColumns.SubGroup,
        ["sousgroupe"] = KnownColumns.SubGroup,
        ["sousGroupe"] = KnownColumns.SubGroup
    };

    private static readonly HashSet<string> KnownColumnNames = new(StringComparer.OrdinalIgnoreCase)
    {
        KnownColumns.Sku,
        KnownColumns.Ean,
        KnownColumns.Name,
        KnownColumns.Description,
        KnownColumns.Group,
        KnownColumns.SubGroup
    };

    private readonly IDbConnection _connection;
    private readonly IClock _clock;
    private readonly ILogger<ProductImportService> _logger;
    private readonly IProductImportMetrics _metrics;
    private readonly IProductGroupRepository _productGroupRepository;

    public ProductImportService(
        IDbConnection connection,
        IClock clock,
        ILogger<ProductImportService> logger,
        IProductImportMetrics metrics,
        IProductGroupRepository productGroupRepository)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _productGroupRepository = productGroupRepository ?? throw new ArgumentNullException(nameof(productGroupRepository));
    }

    public async Task<ProductImportResult> ImportAsync(ProductImportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.CsvStream);

        var bufferedCsv = await BufferStreamAsync(command.CsvStream, cancellationToken).ConfigureAwait(false);
        await using var bufferedStream = bufferedCsv.Stream;

        var encoding = Encoding.Latin1;

        if (_connection is not NpgsqlConnection npgsqlConnection)
        {
            throw new InvalidOperationException("L'import produit requiert une connexion Npgsql active.");
        }

        await EnsureConnectionOpenAsync(npgsqlConnection, cancellationToken).ConfigureAwait(false);

        if (command.ShopId == Guid.Empty)
        {
            throw new InvalidOperationException("Un identifiant de boutique est requis pour lancer l'import produit.");
        }

        var shopId = command.ShopId;

        await using var transaction = await npgsqlConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var lockKey = ComputeAdvisoryLockKey(shopId);
        var lockAcquired = await TryAcquireAdvisoryLockAsync(transaction, lockKey, cancellationToken).ConfigureAwait(false);
        if (!lockAcquired)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new ProductImportInProgressException();
        }

        try
        {
            if (!command.DryRun)
            {
                var alreadyProcessed = await HasImportAlreadyBeenProcessedAsync(transaction, shopId, bufferedCsv.Sha256, cancellationToken).ConfigureAwait(false);
                if (alreadyProcessed)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    LogImportEvent(
                        "ImportCompleted",
                        StatusSkipped,
                        command.Username,
                        bufferedCsv.Sha256,
                        total: 0,
                        created: 0,
                        updated: 0,
                        duration: TimeSpan.Zero,
                        unknownColumns: EmptyUnknownColumns,
                        shopId: shopId);
                    _logger.LogInformation("Import produits ignoré : fichier déjà importé pour la boutique {ShopId}.", shopId);
                    return new ProductImportResult(ProductImportResponse.SkippedResult(), ProductImportResultType.Skipped);
                }
            }

            var historyId = Guid.NewGuid();
            var startedAt = _clock.UtcNow;

            await InsertHistoryStartedAsync(historyId, startedAt, command.Username, bufferedCsv.Sha256, shopId, transaction, cancellationToken)
                .ConfigureAwait(false);

            await transaction.SaveAsync("before_import", cancellationToken).ConfigureAwait(false);

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
            var unknownColumns = parseOutcome.UnknownColumns;

            LogImportEvent(
                "ImportStarted",
                StatusStarted,
                command.Username,
                bufferedCsv.Sha256,
                totalLines,
                created: 0,
                updated: 0,
                duration: TimeSpan.Zero,
                unknownColumns: unknownColumns,
                shopId: shopId);

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
                    created: 0,
                    updated: 0,
                    stopwatch.Elapsed,
                    unknownColumns,
                    shopId);

                return new ProductImportResult(
                    ProductImportResponse.Failure(totalLines, parseOutcome.Errors, unknownColumns, parseOutcome.ProposedGroups),
                    ProductImportResultType.ValidationFailed);
            }

            if (command.DryRun)
            {
                var wouldInsert = parseOutcome.Rows.Count;

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
                    created: wouldInsert,
                    updated: 0,
                    stopwatch.Elapsed,
                    unknownColumns,
                    shopId);

                return new ProductImportResult(
                    ProductImportResponse.DryRunResult(totalLines, wouldInsert, unknownColumns, parseOutcome.ProposedGroups),
                    ProductImportResultType.DryRun);
            }

            try
            {
                var preparedRows = await PrepareRowsAsync(parseOutcome.Rows, shopId, cancellationToken).ConfigureAwait(false);

                ImportStatistics importStats;
                if (command.Mode == ProductImportMode.ReplaceCatalogue)
                {
                    await DeleteExistingProductsAsync(transaction, shopId, cancellationToken).ConfigureAwait(false);
                    importStats = await BulkInsertPreparedRowsAsync(preparedRows, transaction, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    importStats = await UpsertPreparedRowsAsync(preparedRows, transaction, cancellationToken)
                        .ConfigureAwait(false);
                }

                await RecordSuccessfulImportAsync(
                        transaction,
                        shopId,
                        bufferedCsv.Sha256,
                        importStats.TotalAffected,
                        cancellationToken)
                    .ConfigureAwait(false);

                await CompleteHistoryAsync(
                        historyId,
                        StatusSucceeded,
                        totalLines,
                        inserted: importStats.Inserted,
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
                    created: importStats.Inserted,
                    updated: importStats.Updated,
                    stopwatch.Elapsed,
                    unknownColumns,
                    shopId);

                _logger.LogInformation(
                    "Import produits terminé : {Inserted} insertions, {Updated} mises à jour.",
                    importStats.Inserted,
                    importStats.Updated);

                return new ProductImportResult(
                    ProductImportResponse.Success(totalLines, importStats.Inserted, importStats.Updated, unknownColumns, parseOutcome.ProposedGroups),
                    ProductImportResultType.Succeeded);
            }
            catch
            {
                await transaction.RollbackAsync("before_import", cancellationToken).ConfigureAwait(false);

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
                    created: 0,
                    updated: 0,
                    stopwatch.Elapsed,
                    unknownColumns,
                    shopId);

                throw;
            }
        }
        finally
        {
            if (lockAcquired)
            {
                await ReleaseAdvisoryLockAsync(lockKey, cancellationToken).ConfigureAwait(false);
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
        IReadOnlyCollection<string>? unknownColumns,
        Guid shopId)
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
            UnknownColumns = unknownColumns ?? Array.Empty<string>(),
            ShopId = shopId
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
            await destination.DisposeAsync().ConfigureAwait(false);
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

    private async Task<bool> TryAcquireAdvisoryLockAsync(NpgsqlTransaction transaction, long lockKey, CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_try_advisory_lock(@Key);";
        return await _connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { Key = lockKey }, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task ReleaseAdvisoryLockAsync(long lockKey, CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_advisory_unlock(@Key);";
        await _connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { Key = lockKey }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private static long ComputeAdvisoryLockKey(Guid shopId)
    {
        if (shopId == Guid.Empty)
        {
            return GlobalAdvisoryLockKey;
        }

        Span<byte> buffer = stackalloc byte[16];
        if (!shopId.TryWriteBytes(buffer))
        {
            return GlobalAdvisoryLockKey;
        }

        var high = BinaryPrimitives.ReadInt64LittleEndian(buffer[..8]);
        var low = BinaryPrimitives.ReadInt64LittleEndian(buffer[8..]);
        var combined = high ^ low;
        return combined == 0 ? GlobalAdvisoryLockKey : combined;
    }

    private async Task InsertHistoryStartedAsync(
        Guid historyId,
        DateTimeOffset startedAt,
        string? username,
        string fileSha256,
        Guid shopId,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql =
            "INSERT INTO \"ProductImportHistory\" (\"Id\", \"StartedAt\", \"Username\", \"FileSha256\", \"TotalLines\", \"Inserted\", \"ErrorCount\", \"Status\", \"ShopId\") " +
            "VALUES (@Id, @StartedAt, @Username, @FileSha256, 0, 0, 0, @Status, @ShopId);";

        var parameters = new
        {
            Id = historyId,
            StartedAt = startedAt,
            Username = username,
            FileSha256 = fileSha256,
            Status = StatusStarted,
            ShopId = shopId
        };

        await _connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task DeleteExistingProductsAsync(NpgsqlTransaction transaction, Guid shopId, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM \"Product\" WHERE \"ShopId\" = @ShopId;";

        await _connection.ExecuteAsync(
                new CommandDefinition(sql, new { ShopId = shopId }, transaction: transaction, cancellationToken: cancellationToken))
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

    private async Task<bool> HasImportAlreadyBeenProcessedAsync(
        NpgsqlTransaction transaction,
        Guid shopId,
        string fileHash,
        CancellationToken cancellationToken)
    {
        var sql = $"SELECT EXISTS (SELECT 1 FROM \"{ProductImportTable}\" WHERE \"{ProductImportShopIdColumn}\" = @ShopId AND \"{ProductImportFileHashColumn}\" = @FileHash);";

        return await _connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { ShopId = shopId, FileHash = fileHash }, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task RecordSuccessfulImportAsync(
        NpgsqlTransaction transaction,
        Guid shopId,
        string fileHash,
        int rowCount,
        CancellationToken cancellationToken)
    {
        var sql =
            $"INSERT INTO \"{ProductImportTable}\" (\"Id\", \"{ProductImportShopIdColumn}\", \"FileName\", \"{ProductImportFileHashColumn}\", \"{ProductImportRowCountColumn}\", \"ImportedAtUtc\") " +
            "VALUES (@Id, @ShopId, @FileName, @FileHash, @RowCount, @ImportedAtUtc) " +
            $"ON CONFLICT (\"{ProductImportShopIdColumn}\", \"{ProductImportFileHashColumn}\") DO NOTHING;";

        var parameters = new
        {
            Id = Guid.NewGuid(),
            ShopId = shopId,
            FileName = "import.csv",
            FileHash = fileHash,
            RowCount = rowCount,
            ImportedAtUtc = _clock.UtcNow
        };

        await _connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PreparedProductRow>> PrepareRowsAsync(
        IReadOnlyList<ProductCsvRow> rows,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<PreparedProductRow>();
        }

        var now = _clock.UtcNow;
        var prepared = new List<PreparedProductRow>(rows.Count);
        var groupCache = new Dictionary<GroupKey, long?>(GroupKeyComparer.Instance);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sku = (row.Sku ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(sku))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(row.Name) ? sku : row.Name.Trim();
            var description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim();
            var normalizedEan = NormalizeEan(row.Ean);
            var codeDigits = BuildCodeDigits(normalizedEan);
            var attributesJson = SerializeAttributes(row.Attributes, row.SubGroup);
            var groupId = await ResolveGroupIdAsync(row.Group, row.SubGroup, groupCache, cancellationToken)
                .ConfigureAwait(false);

            if (groupId is null && (row.Group is not null || row.SubGroup is not null))
            {
                _logger.LogWarning(
                    "Import: ligne ignorée (sku={Sku}, groupe={Groupe}, sousGroupe={SousGroupe}) — taxonomie introuvable",
                    sku,
                    row.Group,
                    row.SubGroup);
                continue;
            }

            prepared.Add(new PreparedProductRow(
                shopId,
                sku,
                name,
                description,
                normalizedEan,
                groupId,
                attributesJson,
                codeDigits,
                now));
        }

        return prepared;
    }

    private async Task<ImportStatistics> BulkInsertPreparedRowsAsync(
        IReadOnlyList<PreparedProductRow> rows,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return ImportStatistics.Empty;
        }

        if (transaction.Connection is not NpgsqlConnection npgsqlConnection)
        {
            throw new InvalidOperationException("Une connexion Npgsql est requise pour insérer des produits.");
        }

        const string sql = "COPY \"Product\" (\"ShopId\", \"Sku\", \"Name\", \"Description\", \"Ean\", \"GroupId\", \"Attributes\", \"CodeDigits\", \"CreatedAtUtc\") FROM STDIN (FORMAT BINARY);";

        await using var importer = await npgsqlConnection.BeginBinaryImportAsync(sql, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await importer.StartRowAsync(cancellationToken).ConfigureAwait(false);
            importer.Write(row.ShopId, NpgsqlDbType.Uuid);
            importer.Write(row.Sku, NpgsqlDbType.Text);
            importer.Write(row.Name, NpgsqlDbType.Text);

            if (row.Description is null)
            {
                importer.WriteNull();
            }
            else
            {
                importer.Write(row.Description, NpgsqlDbType.Text);
            }

            if (row.Ean is null)
            {
                importer.WriteNull();
            }
            else
            {
                importer.Write(row.Ean, NpgsqlDbType.Text);
            }

            if (row.GroupId is null)
            {
                importer.WriteNull();
            }
            else
            {
                importer.Write(row.GroupId.Value, NpgsqlDbType.Bigint);
            }

            importer.Write(row.AttributesJson, NpgsqlDbType.Jsonb);

            if (row.CodeDigits is null)
            {
                importer.WriteNull();
            }
            else
            {
                importer.Write(row.CodeDigits, NpgsqlDbType.Text);
            }

            importer.Write(row.CreatedAtUtc, NpgsqlDbType.TimestampTz);
        }

        await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);

        return new ImportStatistics(rows.Count, 0);
    }

    private async Task<ImportStatistics> UpsertPreparedRowsAsync(
        IReadOnlyList<PreparedProductRow> rows,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return ImportStatistics.Empty;
        }

        if (transaction.Connection is not NpgsqlConnection npgsqlConnection)
        {
            throw new InvalidOperationException("Une connexion Npgsql est requise pour insérer des produits.");
        }

        const string dropSql = "DROP TABLE IF EXISTS temp_product_import;";
        const string createSql = "CREATE TEMP TABLE temp_product_import (\"ShopId\" uuid NOT NULL, \"Sku\" text NOT NULL, \"Name\" text NOT NULL, \"Description\" text NULL, \"Ean\" text NULL, \"GroupId\" bigint NULL, \"Attributes\" jsonb NOT NULL, \"CodeDigits\" text NULL, \"CreatedAtUtc\" timestamptz NOT NULL) ON COMMIT DROP;";

        await _connection.ExecuteAsync(new CommandDefinition(dropSql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await _connection.ExecuteAsync(new CommandDefinition(createSql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        const string copyTempSql = "COPY temp_product_import (\"ShopId\", \"Sku\", \"Name\", \"Description\", \"Ean\", \"GroupId\", \"Attributes\", \"CodeDigits\", \"CreatedAtUtc\") FROM STDIN (FORMAT BINARY);";
        await using (var tempImporter = await npgsqlConnection.BeginBinaryImportAsync(copyTempSql, cancellationToken).ConfigureAwait(false))
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await tempImporter.StartRowAsync(cancellationToken).ConfigureAwait(false);
                tempImporter.Write(row.ShopId, NpgsqlDbType.Uuid);
                tempImporter.Write(row.Sku, NpgsqlDbType.Text);
                tempImporter.Write(row.Name, NpgsqlDbType.Text);

                if (row.Description is null)
                {
                    tempImporter.WriteNull();
                }
                else
                {
                    tempImporter.Write(row.Description, NpgsqlDbType.Text);
                }

                if (row.Ean is null)
                {
                    tempImporter.WriteNull();
                }
                else
                {
                    tempImporter.Write(row.Ean, NpgsqlDbType.Text);
                }

                if (row.GroupId is null)
                {
                    tempImporter.WriteNull();
                }
                else
                {
                    tempImporter.Write(row.GroupId.Value, NpgsqlDbType.Bigint);
                }

                tempImporter.Write(row.AttributesJson, NpgsqlDbType.Jsonb);

                if (row.CodeDigits is null)
                {
                    tempImporter.WriteNull();
                }
                else
                {
                    tempImporter.Write(row.CodeDigits, NpgsqlDbType.Text);
                }

                tempImporter.Write(row.CreatedAtUtc, NpgsqlDbType.TimestampTz);
            }

            await tempImporter.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        const string upsertSql = """
WITH upsert AS (
    INSERT INTO "Product" AS target ("ShopId", "Sku", "Name", "Description", "Ean", "GroupId", "Attributes", "CodeDigits", "CreatedAtUtc")
    SELECT t."ShopId", t."Sku", t."Name", t."Description", t."Ean", t."GroupId", t."Attributes", t."CodeDigits", t."CreatedAtUtc"
    FROM temp_product_import t
    ON CONFLICT ON CONSTRAINT "UX_Product_Shop_LowerSku" DO UPDATE
    SET
        "Name" = COALESCE(target."Name", EXCLUDED."Name"),
        "Description" = COALESCE(target."Description", EXCLUDED."Description"),
        "Ean" = COALESCE(target."Ean", EXCLUDED."Ean"),
        "GroupId" = EXCLUDED."GroupId",
        "Attributes" = EXCLUDED."Attributes",
        "CodeDigits" = COALESCE(target."CodeDigits", EXCLUDED."CodeDigits")
    RETURNING (xmax = 0) AS inserted
)
SELECT
    COUNT(*) FILTER (WHERE inserted) AS inserted,
    COUNT(*) FILTER (WHERE NOT inserted) AS updated;
""";

        var counts = await _connection.QuerySingleAsync<(int Inserted, int Updated)>(
                new CommandDefinition(upsertSql, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return new ImportStatistics(counts.Inserted, counts.Updated);
    }

    private sealed record ImportStatistics(int Inserted, int Updated)
    {
        public static ImportStatistics Empty { get; } = new(0, 0);

        public int TotalAffected => Inserted + Updated;
    }

    private sealed record PreparedProductRow(
        Guid ShopId,
        string Sku,
        string Name,
        string? Description,
        string? Ean,
        long? GroupId,
        string AttributesJson,
        string? CodeDigits,
        DateTimeOffset CreatedAtUtc);

    private string? NormalizeEan(string? ean)
    {
        if (string.IsNullOrWhiteSpace(ean))
        {
            return null;
        }

        var trimmed = ean.Trim();
        if (trimmed.Length <= 13)
        {
            return trimmed;
        }

        _logger.LogDebug("Code importé trop long ({Length} > 13), colonne EAN laissée vide.", trimmed.Length);
        return null;
    }

    private static string? BuildCodeDigits(string? ean)
    {
        if (string.IsNullOrEmpty(ean))
        {
            return null;
        }

        var digits = DigitsOnlyRegex.Replace(ean, string.Empty);
        return digits.Length == 0 ? null : digits;
    }

    private static string SerializeAttributes(IReadOnlyDictionary<string, object?> attributes, string? subGroup)
    {
        if (attributes.Count == 0 && string.IsNullOrEmpty(subGroup))
        {
            return "{}";
        }

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in attributes)
        {
            payload[kvp.Key] = kvp.Value;
        }

        if (!string.IsNullOrEmpty(subGroup) && !payload.ContainsKey("originalSousGroupe"))
        {
            payload["originalSousGroupe"] = subGroup;
        }

        return JsonSerializer.Serialize(payload, JsonSerializerOptions);
    }

    private async Task<long?> ResolveGroupIdAsync(
        string? group,
        string? subGroup,
        Dictionary<GroupKey, long?> cache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(group) && string.IsNullOrEmpty(subGroup))
        {
            return null;
        }

        var key = new GroupKey(group, subGroup);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var resolved = await _productGroupRepository
            .EnsureGroupAsync(group, subGroup, cancellationToken)
            .ConfigureAwait(false);

        cache[key] = resolved;
        return resolved;
    }

    private async Task<ProductCsvParseOutcome> ParseAsync(Stream stream, Encoding encoding, CancellationToken cancellationToken)
    {
        var rows = new List<ProductCsvRow>();
        var errors = new List<ProductImportError>();
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknownColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var headerCaptured = false;
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var proposedGroups = new List<ProductImportGroupProposal>();
        var proposedGroupsSet = new HashSet<GroupKey>(GroupKeyComparer.Instance);

        var lineNumber = 0;
        var totalLines = 0;
        List<string>? headers = null;

        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } rawLine)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lineNumber++;
            var line = rawLine.TrimEnd('\r');

            if (headers is null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var headerFields = ParseFields(line);
                headers = headerFields.ToList();

                headerMap.Clear();
                for (var i = 0; i < headers.Count; i++)
                {
                    var raw = headers[i];
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    var key = HeaderSynonyms.TryGetValue(raw, out var mapped) ? mapped : raw;
                    if (!headerMap.ContainsKey(key))
                    {
                        headerMap[key] = i;
                    }
                }

                if (!headerMap.ContainsKey(KnownColumns.Sku))
                {
                    errors.Add(new ProductImportError(lineNumber, "MISSING_SKU_COLUMN"));
                }

                if (!headerCaptured)
                {
                    foreach (var key in headerMap.Keys)
                    {
                        if (!KnownColumnNames.Contains(key))
                        {
                            unknownColumns.Add(key);
                        }
                    }

                    headerCaptured = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            totalLines++;

            var fields = ParseFields(line);
            if (fields.Count > headers.Count)
            {
                errors.Add(new ProductImportError(lineNumber, "INVALID_COLUMN_COUNT"));
                continue;
            }

            if (fields.Count < headers.Count)
            {
                while (fields.Count < headers.Count)
                {
                    fields.Add(string.Empty);
                }
            }

            string? sku = null;
            string? ean = null;
            string? name = null;
            string? group = null;
            string? subGroup = null;
            string? description = null;

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var need in new[]
                   {
                       KnownColumns.Sku,
                       KnownColumns.Ean,
                       KnownColumns.Name,
                       KnownColumns.Description,
                       KnownColumns.Group,
                       KnownColumns.SubGroup
                   })
            {
                if (!headerMap.TryGetValue(need, out var idx))
                {
                    continue;
                }

                string value;
                try
                {
                    value = fields[idx] ?? string.Empty;
                }
                catch
                {
                    value = string.Empty;
                }

                row[need] = value.Trim();
            }

            if (row.TryGetValue(KnownColumns.Sku, out var skuValue) && !string.IsNullOrWhiteSpace(skuValue))
            {
                sku ??= skuValue;
            }

            if (row.TryGetValue(KnownColumns.Ean, out var eanValue))
            {
                ean = string.IsNullOrWhiteSpace(eanValue) ? null : eanValue;
            }

            if (row.TryGetValue(KnownColumns.Name, out var nameValue) && !string.IsNullOrWhiteSpace(nameValue))
            {
                name = nameValue;
            }

            if (row.TryGetValue(KnownColumns.Description, out var descriptionValue)
                && !string.IsNullOrWhiteSpace(descriptionValue))
            {
                description = descriptionValue;
            }

            if (row.TryGetValue(KnownColumns.Group, out var groupValue))
            {
                group = NormalizeOptional(groupValue);
            }

            if (row.TryGetValue(KnownColumns.SubGroup, out var subGroupValue))
            {
                subGroup = NormalizeOptional(subGroupValue);
            }

            var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in unknownColumns)
            {
                if (!headerMap.TryGetValue(key, out var idx))
                {
                    continue;
                }

                string value;
                try
                {
                    value = fields[idx] ?? string.Empty;
                }
                catch
                {
                    value = string.Empty;
                }

                value = value.Trim();
                attributes[key] = string.IsNullOrWhiteSpace(value) ? null : value;
            }

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

            name = string.IsNullOrWhiteSpace(name) ? sku : name;

            var normalizedGroup = NormalizeOptional(group);
            var normalizedSubGroup = NormalizeOptional(subGroup);

            if (normalizedGroup is not null || normalizedSubGroup is not null)
            {
                var key = new GroupKey(normalizedGroup, normalizedSubGroup);
                if (proposedGroupsSet.Add(key))
                {
                    proposedGroups.Add(new ProductImportGroupProposal(normalizedGroup, normalizedSubGroup));
                }
            }

            if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(description))
            {
                name = description;
            }

            name ??= sku;

            rows.Add(new ProductCsvRow(
                sku,
                name!,
                description,
                ean,
                normalizedGroup,
                normalizedSubGroup,
                attributes));
        }

        if (headers is null && errors.Count == 0)
        {
            errors.Add(new ProductImportError(0, "MISSING_HEADER"));
        }

        var unknownColumnsImmutable = unknownColumns.Count == 0
            ? EmptyUnknownColumns
            : ImmutableArray.CreateRange(
                unknownColumns.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase));

        var proposedGroupsImmutable = proposedGroups.Count == 0
            ? EmptyProposedGroups
            : proposedGroups.ToImmutableArray();

        return new ProductCsvParseOutcome(rows, errors, totalLines, unknownColumnsImmutable, proposedGroupsImmutable);
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

    private static async Task EnsureConnectionOpenAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.FullState.HasFlag(ConnectionState.Open))
        {
            return;
        }

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record BufferedCsv(MemoryStream Stream, string Sha256);

    private sealed record ProductCsvRow(
        string Sku,
        string Name,
        string? Description,
        string? Ean,
        string? Group,
        string? SubGroup,
        IReadOnlyDictionary<string, object?> Attributes);

    private sealed record ProductCsvParseOutcome(
        IReadOnlyList<ProductCsvRow> Rows,
        IReadOnlyList<ProductImportError> Errors,
        int TotalLines,
        ImmutableArray<string> UnknownColumns,
        ImmutableArray<ProductImportGroupProposal> ProposedGroups)
    {
        public static ProductCsvParseOutcome CreateEmptyFile()
        {
            return new ProductCsvParseOutcome(
                Array.Empty<ProductCsvRow>(),
                new[] { new ProductImportError(0, "EMPTY_FILE") },
                0,
                EmptyUnknownColumns,
                EmptyProposedGroups);
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private sealed record GroupKey(string? Group, string? SubGroup);

    private sealed class GroupKeyComparer : IEqualityComparer<GroupKey>
    {
        public static GroupKeyComparer Instance { get; } = new();

        public bool Equals(GroupKey? x, GroupKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.Group, y.Group, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.SubGroup, y.SubGroup, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(GroupKey obj)
        {
            var groupHash = obj.Group is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Group);
            var subGroupHash = obj.SubGroup is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SubGroup);
            return HashCode.Combine(groupHash, subGroupHash);
        }
    }

    private sealed record UpsertStatistics(int Created, int Updated)
    {
        public static UpsertStatistics Empty { get; } = new(0, 0);
    }

    private static class KnownColumns
    {
        public const string Sku = "sku";
        public const string Ean = "ean";
        public const string Name = "name";
        public const string Description = "description";
        public const string Group = "groupe";
        public const string SubGroup = "sousGroupe";
    }
}
