using System.Data;
using System.Text.Json;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Validation;
using CineBoutique.Inventory.Infrastructure.Database.Products;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace CineBoutique.Inventory.Api.Services.Products.Import;

internal sealed class ProductImportWriter : IProductImportWriter
{
    private readonly IClock _clock;
    private readonly ILogger<ProductImportWriter> _logger;
    private readonly IProductGroupRepository _productGroupRepository;

    public ProductImportWriter(
        IClock clock,
        ILogger<ProductImportWriter> logger,
        IProductGroupRepository productGroupRepository)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _productGroupRepository = productGroupRepository ?? throw new ArgumentNullException(nameof(productGroupRepository));
    }

    public async Task<ProductImportWriteStatistics> PreviewAsync(
        IReadOnlyList<ProductCsvRow> rows,
        Guid shopId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return ProductImportWriteStatistics.Empty;
        }

        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction not bound to a connection.");

        var existingSkus = Array.Empty<string>();

        var distinctSkus = rows
            .Select(row => row.Sku)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctSkus.Length > 0)
        {
            var lowerSkus = distinctSkus
                .Select(static sku => sku.ToLowerInvariant())
                .ToArray();

            const string sql = """
SELECT "Sku"
FROM "Product"
WHERE "ShopId" = @ShopId
  AND LOWER("Sku") = ANY(@LowerSkus);
""";

            var command = new CommandDefinition(
                sql,
                new { ShopId = shopId, LowerSkus = lowerSkus },
                transaction: transaction,
                cancellationToken: cancellationToken);

            existingSkus = (await connection.QueryAsync<string>(command).ConfigureAwait(false)).ToArray();
        }

        var existingSet = new HashSet<string>(existingSkus, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var updated = 0;
        foreach (var row in rows)
        {
            if (existingSet.Contains(row.Sku))
                updated++;
            else
                created++;
        }

        return new ProductImportWriteStatistics(created, updated);
    }

    public async Task<ProductImportWriteStatistics> WriteAsync(
        IReadOnlyList<ProductCsvRow> rows,
        Guid shopId,
        ProductImportMode mode,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return ProductImportWriteStatistics.Empty;

        if (transaction is not NpgsqlTransaction npgsqlTransaction)
            throw new InvalidOperationException("Une transaction Npgsql est requise pour l'import produit.");

        Dictionary<string, string>? preloadedAttributes = null;
        string[] normalizedSkusForDeletion = [];
        string[] normalizedEansForBlocking = [];
        if (mode == ProductImportMode.ReplaceCatalogue)
        {
            preloadedAttributes = await LoadExistingAttributesBySkuAsync(rows, shopId, npgsqlTransaction, cancellationToken)
                .ConfigureAwait(false);

            normalizedSkusForDeletion = rows
                .Select(static row => row.Sku)
                .Where(static sku => !string.IsNullOrWhiteSpace(sku))
                .Select(static sku => sku!.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Keep the blocking detection aligned with legacy behavior: products
            // that have no EAN should not surface as blockers even if their SKU
            // disappears from the incoming file.
            normalizedEansForBlocking = rows
                .Select(row => NormalizeEan(row.Ean))
                .Where(static ean => !string.IsNullOrWhiteSpace(ean))
                .Select(static ean => ean!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await DeleteExistingProductsAsync(
                    shopId,
                    normalizedSkusForDeletion,
                    normalizedEansForBlocking,
                    npgsqlTransaction,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await UpsertRowsAsync(rows, shopId, npgsqlTransaction, cancellationToken, preloadedAttributes)
            .ConfigureAwait(false);
    }

    private async Task<ProductImportWriteStatistics> UpsertRowsAsync(
        IReadOnlyList<ProductCsvRow> rows,
        Guid shopId,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken,
        Dictionary<string, string>? preloadedAttributesBySku)
    {
        if (rows.Count == 0)
            return ProductImportWriteStatistics.Empty;

        var now = _clock.UtcNow;
        var created = 0;
        var updated = 0;
        var groupCache = new Dictionary<ProductImportGroupKey, long?>(ProductImportGroupKeyComparer.Instance);

        var existingAttributesBySku = preloadedAttributesBySku
            ?? await LoadExistingAttributesBySkuAsync(rows, shopId, transaction, cancellationToken).ConfigureAwait(false);

        if (transaction.Connection is not NpgsqlConnection npgsqlConnection)
            throw new InvalidOperationException("Une connexion Npgsql est requise pour l'UPSERT produit.");

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
            var group = ProductImportFieldNormalizer.NormalizeOptional(row.Group);
            var subGroup = ProductImportFieldNormalizer.NormalizeOptional(row.SubGroup);

            if (string.IsNullOrEmpty(sku))
                continue;

            if (string.IsNullOrEmpty(name))
                name = sku;

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
                    created++;
                else
                    updated++;
            }
            else
            {
                _logger.LogError(
                    "Import: résultat inattendu lors de l'upsert du produit {Sku} — booléen attendu, obtenu {Type}",
                    sku,
                    insertedResult?.GetType().FullName ?? "null");
            }
        }

        return new ProductImportWriteStatistics(created, updated);
    }

    private async Task<long?> ResolveGroupIdAsync(
        string? group,
        string? subGroup,
        Dictionary<ProductImportGroupKey, long?> cache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(group) && string.IsNullOrEmpty(subGroup))
            return null;

        var key = new ProductImportGroupKey(group, subGroup);
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var resolved = await _productGroupRepository
            .EnsureGroupAsync(group, subGroup, cancellationToken)
            .ConfigureAwait(false);

        cache[key] = resolved;
        return resolved;
    }

    private async Task<Dictionary<string, string>> LoadExistingAttributesBySkuAsync(
        IReadOnlyList<ProductCsvRow> rows,
        Guid shopId,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (rows.Count == 0)
            return result;

        var lowerSkus = rows
            .Select(static row => row.Sku)
            .Where(static sku => !string.IsNullOrWhiteSpace(sku))
            .Select(static sku => sku!.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();

        if (lowerSkus.Length == 0)
            return result;

        const string sql = """
SELECT "Sku", COALESCE(CAST("Attributes" AS text), '{}') AS attrs
FROM "Product"
WHERE "ShopId" = @ShopId
  AND LOWER("Sku") = ANY(@LowerSkus);
""";

        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction not bound to a connection.");

        var command = new CommandDefinition(
            sql,
            new { ShopId = shopId, LowerSkus = lowerSkus },
            transaction: transaction,
            cancellationToken: cancellationToken);

        var rowsResult = await connection
            .QueryAsync<(string Sku, string Attrs)>(command)
            .ConfigureAwait(false);

        foreach (var (skuValue, attrs) in rowsResult)
            result[skuValue] = attrs;

        return result;
    }

    private static async Task DeleteExistingProductsAsync(
        Guid shopId,
        string[] normalizedSkus,
        string[] normalizedEans,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
DELETE FROM "Product" p
WHERE p."ShopId" = @ShopId
  AND LOWER(p."Sku") <> ALL(@LowerSkus)
  AND NOT EXISTS (
        SELECT 1
        FROM "CountLine" cl
        WHERE cl."ProductId" = p."Id"
    );
""";

        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction not bound to a connection.");

        var command = new CommandDefinition(
            sql,
            new { ShopId = shopId, LowerSkus = normalizedSkus.Length == 0 ? Array.Empty<string>() : normalizedSkus },
            transaction: transaction,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command).ConfigureAwait(false);

        var blocked = await FindBlockedProductsAsync(
                shopId,
                normalizedEans.Length == 0 ? Array.Empty<string>() : normalizedEans,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        if (blocked is { } snapshot)
        {
            throw new ProductImportBlockedException(snapshot.LocationId, snapshot.SampleProductIds);
        }
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

        var digits = ProductImportFieldNormalizer.DigitsOnlyRegex.Replace(ean, string.Empty);
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

        return JsonSerializer.Serialize(payload, ProductImportSerialization.Options);
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
            JsonValueKind.Array or JsonValueKind.Object => JsonSerializer.Deserialize<object?>(
                element.GetRawText(),
                ProductImportSerialization.Options),
            _ => element.GetRawText()
        };
    }

    private static async Task<BlockedProductsSample?> FindBlockedProductsAsync(
        Guid shopId,
        string[] normalizedEans,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT cl."ProductId", cr."LocationId"
FROM "CountLine" cl
JOIN "Product" p ON p."Id" = cl."ProductId"
JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
JOIN "Location" l ON l."Id" = cr."LocationId"
WHERE p."ShopId" = @ShopId
  AND p."Ean" <> ALL(@EanList)
LIMIT 10;
""";

        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction not bound to a connection.");
        var command = new CommandDefinition(
            sql,
            new { ShopId = shopId, EanList = normalizedEans.Length == 0 ? Array.Empty<string>() : normalizedEans },
            transaction: transaction,
            cancellationToken: cancellationToken);

        var rows = (await connection
                .QueryAsync<(Guid ProductId, Guid LocationId)>(command)
                .ConfigureAwait(false))
            .ToList();

        if (rows.Count == 0)
        {
            return null;
        }

        var chosenLocation = rows[0].LocationId;
        var products = rows
            .Where(row => row.LocationId == chosenLocation)
            .Select(row => row.ProductId)
            .Distinct()
            .Take(5)
            .ToArray();

        return new BlockedProductsSample(chosenLocation, products);
    }

    private sealed record BlockedProductsSample(Guid LocationId, IReadOnlyList<Guid> SampleProductIds);
}
