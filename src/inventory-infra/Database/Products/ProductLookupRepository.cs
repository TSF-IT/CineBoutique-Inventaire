using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;

namespace CineBoutique.Inventory.Infrastructure.Database.Products;

public sealed class ProductLookupRepository : IProductLookupRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductLookupRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<ProductLookupItem?> FindBySkuAsync(string sku, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sku))
        {
            return null;
        }

        using var connection = _connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var (projection, _) = await ProductRawCodeMetadata
            .GetRawCodeProjectionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        var sql = $"""
SELECT "Id", "Sku", "Name", "Ean", {projection} AS "Code", "CodeDigits"
FROM "Product"
WHERE "Sku" = @Sku
LIMIT 1;
""";

        return await connection.QuerySingleOrDefaultAsync<ProductLookupItem>(
            new CommandDefinition(sql, new { Sku = sku }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProductLookupItem>> FindByRawCodeAsync(string rawCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(rawCode))
        {
            return Array.Empty<ProductLookupItem>();
        }

        using var connection = _connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var (projection, hasRawCodeColumn) = await ProductRawCodeMetadata
            .GetRawCodeProjectionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        var whereClause = hasRawCodeColumn
            ? "WHERE \"Ean\" = @Code OR \"Code\" = @Code"
            : "WHERE \"Ean\" = @Code";

        var sql = $"""
SELECT "Id", "Sku", "Name", "Ean", {projection} AS "Code", "CodeDigits"
FROM "Product" {whereClause};
""";

        var rows = await connection.QueryAsync<ProductLookupItem>(
            new CommandDefinition(sql, new { Code = rawCode }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToArray();
    }

    public async Task<IReadOnlyList<ProductLookupItem>> FindByCodeDigitsAsync(string digits, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(digits))
        {
            return Array.Empty<ProductLookupItem>();
        }

        using var connection = _connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var (projection, _) = await ProductRawCodeMetadata
            .GetRawCodeProjectionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        const string whereClause = "WHERE \"CodeDigits\" = @Digits";
        var sql = $"""
SELECT "Id", "Sku", "Name", "Ean", {projection} AS "Code", "CodeDigits"
FROM "Product" {whereClause};
""";

        var rows = await connection.QueryAsync<ProductLookupItem>(
            new CommandDefinition(sql, new { Digits = digits }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToArray();
    }

    public async Task<IReadOnlyList<ProductLookupItem>> SearchProductsAsync(
        string code,
        int limit,
        bool hasPaging,
        int pageSize,
        int offset,
        string? sort,
        string? dir,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Array.Empty<ProductLookupItem>();
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var normalizedCode = code.Trim();
        if (normalizedCode.Length == 0)
        {
            return Array.Empty<ProductLookupItem>();
        }

        var effectiveLimit = Math.Clamp(limit, 1, 50);
        var effectivePageSize = Math.Clamp(pageSize, 1, 100);
        var effectiveOffset = Math.Max(0, offset);

        using var connection = _connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var (projection, _) = await ProductRawCodeMetadata
            .GetRawCodeProjectionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        var sql = $"""
WITH input AS (
    SELECT
        LOWER(@Code) AS lowered_code,
        immutable_unaccent(LOWER(@Code)) AS unaccent_code
),
candidate AS (
    SELECT
        p."Id",
        p."Sku",
        p."Name",
        p."Ean",
        {projection} AS "Code",
        p."CodeDigits",
        1 AS match_priority,
        1.0 AS score
    FROM "Product" AS p
    CROSS JOIN input AS i
    WHERE LOWER(p."Sku") LIKE i.lowered_code || '%'

    UNION ALL

    SELECT
        p."Id",
        p."Sku",
        p."Name",
        p."Ean",
        {projection} AS "Code",
        p."CodeDigits",
        2 AS match_priority,
        1.0 AS score
    FROM "Product" AS p
    CROSS JOIN input AS i
    WHERE LOWER(p."Ean") LIKE i.lowered_code || '%'

    UNION ALL

    SELECT
        p."Id",
        p."Sku",
        p."Name",
        p."Ean",
        {projection} AS "Code",
        p."CodeDigits",
        3 AS match_priority,
        similarity(immutable_unaccent(LOWER(p."Name")), i.unaccent_code) * 0.6 AS score
    FROM "Product" AS p
    CROSS JOIN input AS i
    WHERE similarity(immutable_unaccent(LOWER(p."Name")), i.unaccent_code) > 0.2

    UNION ALL

    SELECT
        p."Id",
        p."Sku",
        p."Name",
        p."Ean",
        {projection} AS "Code",
        p."CodeDigits",
        4 AS match_priority,
        similarity(immutable_unaccent(LOWER(pg."Label")), i.unaccent_code) * 0.4 AS score
    FROM "Product" AS p
    JOIN "ProductGroup" AS pg ON pg."Id" = p."GroupId"
    CROSS JOIN input AS i
    WHERE pg."Label" IS NOT NULL
      AND similarity(immutable_unaccent(LOWER(pg."Label")), i.unaccent_code) > 0.2
),
ranked AS (
    SELECT DISTINCT ON (candidate."Sku")
        candidate."Id",
        candidate."Sku",
        candidate."Name",
        candidate."Ean",
        candidate."Code",
        candidate."CodeDigits",
        candidate.match_priority,
        candidate.score
    FROM candidate
    ORDER BY candidate."Sku", candidate.match_priority, candidate.score DESC, candidate."Name"
)
SELECT
    ranked."Id",
    ranked."Sku",
    ranked."Name",
    ranked."Ean",
    ranked."Code",
    ranked."CodeDigits",
    ranked.match_priority AS "MatchPriority",
    ranked.score AS "Score"
FROM ranked
""";

        var key = (sort ?? string.Empty).ToLowerInvariant();
        var col = key switch
        {
            "name" => @"""Name""",
            "ean"  => @"""Ean""",
            "sku"  => @"""Sku""",
            _      => null
        };
        var direction = (dir ?? "asc").ToLowerInvariant() == "desc" ? "DESC" : "ASC";
        var orderByClause = col is null ? string.Empty : $" ORDER BY {col} {direction} ";
        const string defaultOrderClause = " ORDER BY ranked.match_priority, ranked.score DESC, ranked.\"Name\"";
        var finalOrderClause = string.IsNullOrEmpty(orderByClause) ? defaultOrderClause : orderByClause;
        var finalSqlCore = sql + finalOrderClause;

        if (!hasPaging)
        {
            finalSqlCore += " LIMIT @Limit";
        }

        string finalSql;
        object finalParams;
        if (hasPaging)
        {
            finalSql = finalSqlCore + " LIMIT @limit OFFSET @offset";
            finalParams = new
            {
                Code = normalizedCode,
                Limit = effectiveLimit,
                limit = effectivePageSize,
                offset = effectiveOffset
            };
        }
        else
        {
            finalSql = finalSqlCore;
            finalParams = new { Code = normalizedCode, Limit = effectiveLimit };
        }

        var command = new CommandDefinition(
            finalSql,
            finalParams,
            cancellationToken: cancellationToken);

        var rows = await connection
            .QueryAsync<ProductSearchQueryRow>(command)
            .ConfigureAwait(false);

        return rows
            .Select(row => new ProductLookupItem(row.Id, row.Sku, row.Name, row.Ean, row.Code, row.CodeDigits))
            .ToArray();
    }

    private static async Task EnsureConnectionOpenAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        switch (connection)
        {
            case DbConnection dbConnection when dbConnection.State != ConnectionState.Open:
                await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                break;
            case { State: ConnectionState.Closed }:
                connection.Open();
                break;
        }
    }

    private static string TrimLimitClause(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return sql;
        }

        var trimmed = sql.TrimEnd();
        if (trimmed.EndsWith(";", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        const string limitClause = "LIMIT @Limit";
        var index = trimmed.LastIndexOf(limitClause, StringComparison.Ordinal);
        if (index >= 0)
        {
            trimmed = trimmed[..index].TrimEnd();
        }

        return trimmed;
    }
}
