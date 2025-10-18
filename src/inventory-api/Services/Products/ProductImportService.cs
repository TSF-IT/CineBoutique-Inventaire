using System;
using System.Buffers;
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
using CineBoutique.Inventory.Infrastructure.Database.Products;
using NpgsqlTypes;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportService : IProductImportService
{
    private const long MaxCsvSizeBytes = 25L * 1024L * 1024L;
    private const long AdvisoryLockKey = 297351;
    private const string StatusStarted = "Started";
    private const string StatusSucceeded = "Succeeded";
    private const string StatusFailed = "Failed";
    private const string StatusDryRun = "DryRun";
    private const string StatusSkipped = "Skipped";

    private static readonly Regex DigitsOnlyRegex = new("[^0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly ImmutableArray<string> EmptyUnknownColumns = ImmutableArray<string>.Empty;
    private static readonly ImmutableArray<ProductImportGroupProposal> EmptyProposedGroups = ImmutableArray<ProductImportGroupProposal>.Empty;

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
        ["descr"] = KnownColumns.Name,
        ["description"] = KnownColumns.Name,
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
                        created: 0,
                        updated: 0,
                        duration: TimeSpan.Zero,
                        unknownColumns: EmptyUnknownColumns);
                    _logger.LogInformation("Import produits ignoré : fichier identique au dernier import réussi.");
                    return new ProductImportResult(ProductImportResponse.SkippedResult(), ProductImportResultType.Skipped);
                }
            }

            var hasSuccessfulImport = await HasSuccessfulImportAsync(transaction, cancellationToken).ConfigureAwait(false);

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
                unknownColumns: unknownColumns);

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
                    unknownColumns);

                return new ProductImportResult(
                    ProductImportResponse.Failure(totalLines, parseOutcome.Errors, unknownColumns, parseOutcome.ProposedGroups),
                    ProductImportResultType.ValidationFailed);
            }

            if (command.DryRun)
            {
                var preview = await ComputeUpsertPreviewAsync(parseOutcome.Rows, transaction, cancellationToken)
                    .ConfigureAwait(false);

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
                    created: preview.Created,
                    updated: preview.Updated,
                    stopwatch.Elapsed,
                    unknownColumns);

                return new ProductImportResult(
                    ProductImportResponse.DryRunResult(totalLines, preview.Created, unknownColumns, parseOutcome.ProposedGroups),
                    ProductImportResultType.DryRun);
            }

            try
            {
                if (!command.DryRun && !hasSuccessfulImport && parseOutcome.Rows.Count > 0)
                {
                    await DeleteExistingProductsAsync(transaction, cancellationToken).ConfigureAwait(false);
                }

                var upsertStats = await UpsertRowsAsync(parseOutcome.Rows, transaction, cancellationToken).ConfigureAwait(false);

                await CompleteHistoryAsync(
                        historyId,
                        StatusSucceeded,
                        totalLines,
                        inserted: upsertStats.Created + upsertStats.Updated,
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
                    created: upsertStats.Created,
                    updated: upsertStats.Updated,
                    stopwatch.Elapsed,
                    unknownColumns);

                _logger.LogInformation(
                    "Import produits terminé : {Created} créations, {Updated} mises à jour.",
                    upsertStats.Created,
                    upsertStats.Updated);

                return new ProductImportResult(
                    ProductImportResponse.Success(totalLines, upsertStats.Created, upsertStats.Updated, unknownColumns, parseOutcome.ProposedGroups),
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
        IReadOnlyCollection<string>? unknownColumns)
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

    private async Task<bool> HasSuccessfulImportAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql =
            "SELECT EXISTS (" +
            "SELECT 1 FROM \"ProductImportHistory\" " +
            "WHERE \"Status\" = @StatusSucceeded" +
            ");";

        return await _connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { StatusSucceeded }, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task DeleteExistingProductsAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM \"Product\";";

        await _connection.ExecuteAsync(
                new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken))
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

    private async Task<UpsertStatistics> UpsertRowsAsync(
        IReadOnlyList<ProductCsvRow> rows,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return UpsertStatistics.Empty;
        }

        var now = _clock.UtcNow;
        var created = 0;
        var updated = 0;
        var groupCache = new Dictionary<GroupKey, long?>(GroupKeyComparer.Instance);

        if (transaction.Connection is not NpgsqlConnection npgsqlConnection)
        {
            throw new InvalidOperationException("Une connexion Npgsql est requise pour l'UPSERT produit.");
        }

        const string sql = """
INSERT INTO "Product" ("Sku", "Name", "Ean", "GroupId", "Attributes", "CodeDigits", "CreatedAtUtc")
VALUES (@sku, @name, @ean, @gid, @attrs, @digits, @created)
ON CONFLICT ((LOWER("Sku")))
DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "Ean" = EXCLUDED."Ean",
    "GroupId" = EXCLUDED."GroupId",
    "Attributes" = COALESCE("Product"."Attributes", '{}'::jsonb) || EXCLUDED."Attributes",
    "CodeDigits" = EXCLUDED."CodeDigits"
RETURNING (xmax = 0) AS inserted;
""";

        await using var command = new NpgsqlCommand(sql, npgsqlConnection, transaction);

        var skuParameter = command.Parameters.Add("sku", NpgsqlDbType.Text);
        var nameParameter = command.Parameters.Add("name", NpgsqlDbType.Text);
        var eanParameter = command.Parameters.Add("ean", NpgsqlDbType.Text);
        var groupParameter = command.Parameters.Add("gid", NpgsqlDbType.Bigint);
        var attributesParameter = command.Parameters.Add("attrs", NpgsqlDbType.Jsonb);
        var digitsParameter = command.Parameters.Add("digits", NpgsqlDbType.Text);
        var createdParameter = command.Parameters.Add("created", NpgsqlDbType.TimestampTz);

        skuParameter.Value = string.Empty;
        nameParameter.Value = string.Empty;
        eanParameter.Value = DBNull.Value;
        groupParameter.Value = DBNull.Value;
        attributesParameter.Value = "{}";
        digitsParameter.Value = DBNull.Value;
        createdParameter.Value = now;

        await command.PrepareAsync(cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            var sku = (row.Sku ?? string.Empty).Trim();
            var ean = string.IsNullOrWhiteSpace(row.Ean) ? null : row.Ean.Trim();
            var name = (row.Name ?? string.Empty).Trim();
            var group = NormalizeOptional(row.Group);
            var subGroup = NormalizeOptional(row.SubGroup);

            if (string.IsNullOrEmpty(sku))
            {
                continue;
            }

            if (string.IsNullOrEmpty(name))
            {
                name = sku;
            }

            var normalizedEan = NormalizeEan(ean);
            var codeDigits = BuildCodeDigits(ean ?? normalizedEan);
            var attributesJson = SerializeAttributes(row.Attributes, subGroup);
            var groupId = await ResolveGroupIdAsync(group, subGroup, groupCache, cancellationToken)
                .ConfigureAwait(false);

            skuParameter.Value = sku;
            nameParameter.Value = name;
            eanParameter.Value = normalizedEan ?? (object)DBNull.Value;
            groupParameter.Value = groupId ?? (object)DBNull.Value;
            attributesParameter.Value = attributesJson;
            digitsParameter.Value = codeDigits ?? (object)DBNull.Value;
            createdParameter.Value = now;

            var inserted = (bool)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (inserted)
            {
                created++;
            }
            else
            {
                updated++;
            }
        }

        return new UpsertStatistics(created, updated);
    }

    private async Task<UpsertStatistics> ComputeUpsertPreviewAsync(
        IReadOnlyList<ProductCsvRow> rows,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return UpsertStatistics.Empty;
        }

        var distinctSkus = rows
            .Select(row => row.Sku)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lowerSkus = distinctSkus
            .Select(static sku => sku.ToLowerInvariant())
            .ToArray();

        const string sql = "SELECT \"Sku\" FROM \"Product\" WHERE LOWER(\"Sku\") = ANY(@LowerSkus);";
        var existingSkus = await _connection.QueryAsync<string>(
                new CommandDefinition(sql, new { LowerSkus = lowerSkus }, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var existingSet = new HashSet<string>(existingSkus, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var updated = 0;
        foreach (var row in rows)
        {
            if (existingSet.Contains(row.Sku))
            {
                updated++;
            }
            else
            {
                created++;
            }
        }

        return new UpsertStatistics(created, updated);
    }

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

    private static string SerializeAttributes(IReadOnlyDictionary<string, string> attributes, string? subGroup)
    {
        if (attributes.Count == 0 && string.IsNullOrEmpty(subGroup))
        {
            return "{}";
        }

        if (string.IsNullOrEmpty(subGroup))
        {
            return JsonSerializer.Serialize(attributes, JsonSerializerOptions);
        }

        var payload = new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase)
        {
            ["originalSousGroupe"] = subGroup
        };

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
        var unknownColumns = new List<string>();
        var unknownColumnsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proposedGroups = new List<ProductImportGroupProposal>();
        var proposedGroupsSet = new HashSet<GroupKey>(GroupKeyComparer.Instance);

        var lineNumber = 0;
        var totalLines = 0;
        List<HeaderDefinition>? headers = null;

        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } rawLine)
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
                headers = BuildHeaders(headerFields, unknownColumns, unknownColumnsSet);

                if (!headers.Any(header => string.Equals(header.Target, KnownColumns.Sku, StringComparison.Ordinal)))
                {
                    errors.Add(new ProductImportError(lineNumber, "MISSING_SKU_COLUMN"));
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
            var rowDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < headers.Count; i++)
            {
                var header = headers[i];
                var value = fields[i].Trim();
                var key = header.Target ?? header.Original;

                if (!string.IsNullOrEmpty(key))
                {
                    rowDict[key] = value;
                }

                if (header.Target is null)
                {
                    continue;
                }

                switch (header.Target)
                {
                    case KnownColumns.Sku:
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            sku ??= value.Trim();
                        }

                        break;
                    case KnownColumns.Ean:
                        ean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                        break;
                    case KnownColumns.Name:
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            name = value.Trim();
                        }

                        break;
                    case KnownColumns.Group:
                        group = NormalizeOptional(value);
                        break;
                    case KnownColumns.SubGroup:
                        subGroup = NormalizeOptional(value);
                        break;
                }
            }

            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in rowDict)
            {
                if (KnownColumnNames.Contains(kvp.Key) || string.IsNullOrEmpty(kvp.Value))
                {
                    continue;
                }

                attributes[kvp.Key] = kvp.Value;
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

            rows.Add(new ProductCsvRow(
                sku,
                name!,
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
            : unknownColumns.ToImmutableArray();

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
        string? Ean,
        string? Group,
        string? SubGroup,
        IReadOnlyDictionary<string, string> Attributes);

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

    private static List<HeaderDefinition> BuildHeaders(
        IReadOnlyList<string> headerFields,
        List<string> unknownColumns,
        HashSet<string> unknownColumnsSet)
    {
        var headers = new List<HeaderDefinition>(headerFields.Count);
        var assignedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in headerFields)
        {
            var original = field.Trim();
            var normalized = NormalizeHeader(original);

            if (normalized is not null && !assignedTargets.Add(normalized))
            {
                normalized = null;
            }

            if (normalized is null && !string.IsNullOrEmpty(original) && unknownColumnsSet.Add(original))
            {
                unknownColumns.Add(original);
            }

            headers.Add(new HeaderDefinition(original, normalized));
        }

        return headers;
    }

    private static string? NormalizeHeader(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        return HeaderSynonyms.TryGetValue(header.Trim(), out var normalized) ? normalized : null;
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

    private sealed record HeaderDefinition(string Original, string? Target);

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
        public const string Group = "groupe";
        public const string SubGroup = "sousGroupe";
    }
}
