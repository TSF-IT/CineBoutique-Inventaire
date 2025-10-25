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
using CineBoutique.Inventory.Api.Infrastructure.Logging;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Validation;
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
    private const long AdvisoryLockKey = 297351;
    private const string StatusStarted = "Started";
    private const string StatusSucceeded = "Succeeded";
    private const string StatusFailed = "Failed";
    private const string StatusDryRun = "DryRun";

    private static readonly Regex DigitsOnlyRegex = new("[^0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
    private static readonly ImmutableArray<string> EmptyUnknownColumns = ImmutableArray<string>.Empty;
    private static readonly ImmutableArray<ProductImportGroupProposal> EmptyProposedGroups = ImmutableArray<ProductImportGroupProposal>.Empty;
    private static readonly ImmutableArray<ProductImportSkippedLine> EmptySkippedLines = ImmutableArray<ProductImportSkippedLine>.Empty;

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
        ["rfid"] = KnownColumns.Ean,
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

        if (command.ShopId == Guid.Empty)
        {
            throw new ArgumentException("Shop identifier is required for product imports.", nameof(command));
        }

        var shopId = command.ShopId;

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
            var historyId = Guid.NewGuid();
            var startedAt = _clock.UtcNow;

            await InsertHistoryStartedAsync(historyId, shopId, startedAt, command.Username, bufferedCsv.Sha256, transaction, cancellationToken)
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
                        elapsed: stopwatch.Elapsed,
                        cancellationToken)
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
                var preview = await ComputeUpsertPreviewAsync(parseOutcome.Rows, shopId, transaction, cancellationToken)
                    .ConfigureAwait(false);

                await CompleteHistoryAsync(
                        historyId,
                        StatusDryRun,
                        totalLines,
                        inserted: 0,
                        errorCount: 0,
                        transaction,
                        elapsed: stopwatch.Elapsed,
                        cancellationToken)
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
                Dictionary<string, string>? preloadedAttributes = null;

                if (!command.DryRun && command.Mode == ProductImportMode.ReplaceCatalogue)
                {
                    preloadedAttributes = await LoadExistingAttributesBySkuAsync(parseOutcome.Rows, shopId, transaction, cancellationToken)
                        .ConfigureAwait(false);

                    await DeleteExistingProductsAsync(shopId, transaction, cancellationToken).ConfigureAwait(false);
                }

                var upsertStats = await UpsertRowsAsync(parseOutcome.Rows, shopId, transaction, cancellationToken, preloadedAttributes)
                    .ConfigureAwait(false);

                await CompleteHistoryAsync(
                        historyId,
                        StatusSucceeded,
                        totalLines,
                        inserted: upsertStats.Created + upsertStats.Updated,
                        errorCount: 0,
                        transaction,
                        elapsed: stopwatch.Elapsed,
                        cancellationToken)
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

                ApiLog.ImportStep(
                    _logger,
                    $"Import produits terminé : {upsertStats.Created} créations, {upsertStats.Updated} mises à jour.");

                return new ProductImportResult(
                    ProductImportResponse.Success(
                        totalLines,
                        upsertStats.Created,
                        upsertStats.Updated,
                        unknownColumns,
                        parseOutcome.ProposedGroups,
                        parseOutcome.SkippedLines,
                        parseOutcome.Duplicates),
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
                        elapsed: stopwatch.Elapsed,
                        cancellationToken)
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

        ApiLog.ImportStep(_logger, $"{eventName} {JsonSerializer.Serialize(payload, JsonSerializerOptions)}");
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

    private async Task InsertHistoryStartedAsync(
        Guid historyId,
        Guid shopId,
        DateTimeOffset startedAt,
        string? username,
        string fileSha256,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql =
            "INSERT INTO \"ProductImportHistory\" (\"Id\", \"ShopId\", \"StartedAt\", \"Username\", \"FileSha256\", \"TotalLines\", \"Inserted\", \"ErrorCount\", \"Status\") " +
            "VALUES (@Id, @ShopId, @StartedAt, @Username, @FileSha256, 0, 0, 0, @Status);";

        var parameters = new
        {
            Id = historyId,
            ShopId = shopId,
            StartedAt = startedAt,
            Username = username,
            FileSha256 = fileSha256,
            Status = StatusStarted
        };

        await _connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task DeleteExistingProductsAsync(Guid shopId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM \"Product\" WHERE \"ShopId\" = @ShopId;";

        await _connection.ExecuteAsync(
                new CommandDefinition(sql, new { ShopId = shopId }, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task<Dictionary<string, string>> LoadExistingAttributesBySkuAsync(
        IReadOnlyList<ProductCsvRow> rows,
        Guid shopId,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var existingAttributesBySku = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (rows.Count == 0)
        {
            return existingAttributesBySku;
        }

        var lowerSkus = rows
            .Select(static row => row.Sku)
            .Where(static sku => !string.IsNullOrWhiteSpace(sku))
            .Select(static sku => sku!.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();

        if (lowerSkus.Length == 0)
        {
            return existingAttributesBySku;
        }

        const string existingAttrsSql =
            "SELECT \"Sku\", COALESCE(CAST(\"Attributes\" AS text), '{}') AS attrs " +
            "FROM \"Product\" WHERE \"ShopId\" = @ShopId AND LOWER(\"Sku\") = ANY(@LowerSkus);";

        var existingRows = await _connection.QueryAsync<(string Sku, string Attrs)>(
                new CommandDefinition(existingAttrsSql, new { ShopId = shopId, LowerSkus = lowerSkus }, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        foreach (var (skuValue, attrs) in existingRows)
        {
            existingAttributesBySku[skuValue] = attrs;
        }

        return existingAttributesBySku;
    }

    private async Task CompleteHistoryAsync(
        Guid historyId,
        string status,
        int totalLines,
        int inserted,
        int errorCount,
        NpgsqlTransaction transaction,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
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
        Guid shopId,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken,
        Dictionary<string, string>? preloadedAttributesBySku)
    {
        if (rows.Count == 0)
        {
            return UpsertStatistics.Empty;
        }

        var now = _clock.UtcNow;
        var created = 0;
        var updated = 0;
        var groupCache = new Dictionary<GroupKey, long?>(GroupKeyComparer.Instance);

        var existingAttributesBySku = preloadedAttributesBySku
            ?? await LoadExistingAttributesBySkuAsync(rows, shopId, transaction, cancellationToken).ConfigureAwait(false);

        if (transaction.Connection is not NpgsqlConnection npgsqlConnection)
        {
            throw new InvalidOperationException("Une connexion Npgsql est requise pour l'UPSERT produit.");
        }

        const string sql = """
INSERT INTO "Product" ("ShopId", "Sku", "Name", "Ean", "GroupId", "Attributes", "CodeDigits", "CreatedAtUtc")
VALUES (@shopId, @sku, @name, @ean, @gid, @attrs, @digits, @created)
ON CONFLICT ("ShopId", LOWER("Sku"))
DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "Ean" = EXCLUDED."Ean",
    "GroupId" = EXCLUDED."GroupId",
    "Attributes" = COALESCE("Product"."Attributes", '{}'::jsonb) || EXCLUDED."Attributes",
    "CodeDigits" = EXCLUDED."CodeDigits"
RETURNING (xmax = 0) AS inserted;
""";

        await using var command = new NpgsqlCommand(sql, npgsqlConnection, transaction);

        var shopParameter = command.Parameters.Add("shopId", NpgsqlDbType.Uuid);
        var skuParameter = command.Parameters.Add("sku", NpgsqlDbType.Text);
        var nameParameter = command.Parameters.Add("name", NpgsqlDbType.Text);
        var eanParameter = command.Parameters.Add("ean", NpgsqlDbType.Text);
        var groupParameter = command.Parameters.Add("gid", NpgsqlDbType.Bigint);
        var attributesParameter = command.Parameters.Add("attrs", NpgsqlDbType.Jsonb);
        var digitsParameter = command.Parameters.Add("digits", NpgsqlDbType.Text);
        var createdParameter = command.Parameters.Add("created", NpgsqlDbType.TimestampTz);

        shopParameter.Value = shopId;
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
            existingAttributesBySku.TryGetValue(sku, out var existingAttrsJson);
            var attributesJson = SerializeAttributes(row.Attributes, subGroup, existingAttrsJson);
            existingAttributesBySku[sku] = attributesJson;
            var groupId = await ResolveGroupIdAsync(group, subGroup, groupCache, cancellationToken)
                .ConfigureAwait(false);

            if (groupId is null && (group is not null || subGroup is not null))
            {
                _logger.LogWarning(
                    "Import: ligne ignorée (sku={Sku}, groupe={Groupe}, sousGroupe={SousGroupe}) — taxonomie introuvable",
                    sku,
                    group,
                    subGroup);
                continue;
            }

            skuParameter.Value = sku;
            nameParameter.Value = name;
            eanParameter.Value = normalizedEan ?? (object)DBNull.Value;
            groupParameter.Value = groupId ?? (object)DBNull.Value;
            attributesParameter.Value = attributesJson;
            digitsParameter.Value = codeDigits ?? (object)DBNull.Value;
            createdParameter.Value = now;

            var insertedResult = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (insertedResult is bool inserted)
            {
                if (inserted)
                {
                    created++;
                }
                else
                {
                    updated++;
                }
            }
            else
            {
                _logger.LogError(
                    "Import: résultat inattendu lors de l'upsert du produit {Sku} — booléen attendu, obtenu {Type}",
                    sku,
                    insertedResult?.GetType().FullName ?? "null");
                continue;
            }
        }

        return new UpsertStatistics(created, updated);
    }

    private async Task<UpsertStatistics> ComputeUpsertPreviewAsync(
        IReadOnlyList<ProductCsvRow> rows,
        Guid shopId,
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

        const string sql = "SELECT \"Sku\" FROM \"Product\" WHERE \"ShopId\" = @ShopId AND LOWER(\"Sku\") = ANY(@LowerSkus);";
        var existingSkus = await _connection.QueryAsync<string>(
                new CommandDefinition(sql, new { ShopId = shopId, LowerSkus = lowerSkus }, transaction: transaction, cancellationToken: cancellationToken))
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
        var normalized = InventoryCodeValidator.Normalize(ean);
        if (normalized is null)
        {
            return null;
        }

        if (!InventoryCodeValidator.TryValidate(normalized, out var validationError))
        {
            _logger.LogDebug("Code importé ignoré: {Reason}", validationError);
            return null;
        }

        return normalized;
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

    private static string SerializeAttributes(
        IReadOnlyDictionary<string, object?> attributes,
        string? subGroup,
        string? existingAttributesJson)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(existingAttributesJson))
        {
            MergeExistingAttributes(existingAttributesJson!, payload);
        }

        foreach (var kvp in attributes)
        {
            payload[kvp.Key] = kvp.Value;
        }

        if (!string.IsNullOrEmpty(subGroup) && !payload.ContainsKey("originalSousGroupe"))
        {
            payload["originalSousGroupe"] = subGroup;
        }

        if (payload.Count == 0)
        {
            return "{}";
        }

        return JsonSerializer.Serialize(payload, JsonSerializerOptions);
    }

    private static void MergeExistingAttributes(string existingAttributesJson, IDictionary<string, object?> payload)
    {
        try
        {
            using var document = JsonDocument.Parse(existingAttributesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                payload[property.Name] = ConvertJsonElement(property.Value);
            }
        }
        catch (JsonException)
        {
            // Ignore invalid JSON stored in Attributes to avoid breaking imports.
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Array or JsonValueKind.Object => JsonSerializer.Deserialize<object?>(element.GetRawText(), JsonSerializerOptions),
            _ => element.GetRawText()
        };
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
        var skippedLines = new List<ProductImportSkippedLine>();
        var unknownColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var headerCaptured = false;
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var proposedGroups = new List<ProductImportGroupProposal>();
        var proposedGroupsSet = new HashSet<GroupKey>(GroupKeyComparer.Instance);

        var lineNumber = 0;
        var totalLines = 0;
        List<string>? headers = null;

        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

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

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var need in new[]
                   {
                       KnownColumns.Sku,
                       KnownColumns.Ean,
                       KnownColumns.Name,
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
                attributes,
                lineNumber,
                line));
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

        var skippedLinesImmutable = skippedLines.Count == 0
            ? EmptySkippedLines
            : skippedLines.ToImmutableArray();

        var duplicateReport = BuildDuplicateReport(rows);

        return new ProductCsvParseOutcome(
            rows,
            errors,
            totalLines,
            unknownColumnsImmutable,
            proposedGroupsImmutable,
            skippedLinesImmutable,
            duplicateReport);
    }

    private ProductImportDuplicateReport BuildDuplicateReport(IReadOnlyList<ProductCsvRow> rows)
    {
        if (rows.Count == 0)
        {
            return ProductImportDuplicateReport.Empty;
        }

        var duplicateSkus = rows
            .GroupBy(row => row.Sku, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => ProductImportDuplicateEntry.Create(
                group.Key,
                group.Select(row => row.LineNumber).OrderBy(n => n),
                group.Select(row => row.RawLine)))
            .OrderBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duplicateEans = rows
            .Select(row => new
            {
                Row = row,
                Key = NormalizeEanForDuplicates(row.Ean)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group =>
            {
                var displayValue = group.First().Row.Ean ?? group.Key;
                return ProductImportDuplicateEntry.Create(
                    displayValue,
                    group.Select(x => x.Row.LineNumber).OrderBy(n => n),
                    group.Select(x => x.Row.RawLine));
            })
            .OrderBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (duplicateSkus.Count == 0 && duplicateEans.Count == 0)
        {
            return ProductImportDuplicateReport.Empty;
        }

        return ProductImportDuplicateReport.Create(duplicateSkus, duplicateEans);
    }

    private static string? NormalizeEanForDuplicates(string? ean)
    {
        if (string.IsNullOrWhiteSpace(ean))
        {
            return null;
        }

        var normalized = InventoryCodeValidator.Normalize(ean);
        if (normalized is not null)
        {
            return normalized;
        }

        var digits = DigitsOnlyRegex.Replace(ean, string.Empty);
        if (digits.Length > 0)
        {
            return digits;
        }

        return ean.Trim();
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
        IReadOnlyDictionary<string, object?> Attributes,
        int LineNumber,
        string RawLine);

    private sealed record ProductCsvParseOutcome(
        IReadOnlyList<ProductCsvRow> Rows,
        IReadOnlyList<ProductImportError> Errors,
        int TotalLines,
        ImmutableArray<string> UnknownColumns,
        ImmutableArray<ProductImportGroupProposal> ProposedGroups,
        ImmutableArray<ProductImportSkippedLine> SkippedLines,
        ProductImportDuplicateReport Duplicates)
    {
        public static ProductCsvParseOutcome CreateEmptyFile()
        {
            return new ProductCsvParseOutcome(
                Array.Empty<ProductCsvRow>(),
                new[] { new ProductImportError(0, "EMPTY_FILE") },
                0,
                EmptyUnknownColumns,
                EmptyProposedGroups,
                EmptySkippedLines,
                ProductImportDuplicateReport.Empty);
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
        public const string Group = "groupe";
        public const string SubGroup = "sousGroupe";
    }
}
