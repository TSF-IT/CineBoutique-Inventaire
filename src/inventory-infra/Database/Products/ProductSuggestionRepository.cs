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

public sealed class ProductSuggestionRepository : IProductSuggestionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductSuggestionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<ProductSuggestionItem>> SuggestAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        using var connection = _connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var supportsRawCode = await ProductRawCodeMetadata
            .SupportsRawCodeColumnAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        var rawCodeClause = BuildRawCodeClause(supportsRawCode);

        var sql = $"""
WITH input AS (
    SELECT @Query::text AS q,
           LEAST(GREATEST(@Limit, 1), 50) AS limit
),
normalized AS (
    SELECT
        q,
        limit,
        LOWER(q) AS q_lower,
        immutable_unaccent(LOWER(q)) AS q_unaccent
    FROM input
),
candidates AS (
    SELECT
        p."Sku",
        p."Ean",
        p."Name",
        parent."Label" AS "GroupLabel",
        pg."Label" AS "SubGroupLabel",
        1.0::double precision AS score
    FROM normalized n
    JOIN "Product" p ON p."Sku" ILIKE n.q || '%'
    LEFT JOIN "ProductGroup" pg ON pg."Id" = p."GroupId"
    LEFT JOIN "ProductGroup" parent ON parent."Id" = pg."ParentId"

    UNION ALL

    SELECT
        p."Sku",
        p."Ean",
        p."Name",
        parent."Label" AS "GroupLabel",
        pg."Label" AS "SubGroupLabel",
        1.0::double precision AS score
    FROM normalized n
    JOIN "Product" p ON p."Ean" ILIKE n.q || '%'
    LEFT JOIN "ProductGroup" pg ON pg."Id" = p."GroupId"
    LEFT JOIN "ProductGroup" parent ON parent."Id" = pg."ParentId"
    WHERE p."Ean" IS NOT NULL
{rawCodeClause}

    UNION ALL

    SELECT
        p."Sku",
        p."Ean",
        p."Name",
        parent."Label" AS "GroupLabel",
        pg."Label" AS "SubGroupLabel",
        similarity(immutable_unaccent(LOWER(p."Name")), n.q_unaccent) * 0.6 AS score
    FROM normalized n
    JOIN "Product" p ON p."Name" IS NOT NULL
    LEFT JOIN "ProductGroup" pg ON pg."Id" = p."GroupId"
    LEFT JOIN "ProductGroup" parent ON parent."Id" = pg."ParentId"
    WHERE immutable_unaccent(LOWER(p."Name")) % n.q_unaccent

    UNION ALL

    SELECT
        p."Sku",
        p."Ean",
        p."Name",
        parent."Label" AS "GroupLabel",
        pg."Label" AS "SubGroupLabel",
        similarity(immutable_unaccent(LOWER(pg."Label")), n.q_unaccent) * 0.4 AS score
    FROM normalized n
    JOIN "Product" p ON p."GroupId" IS NOT NULL
    JOIN "ProductGroup" pg ON pg."Id" = p."GroupId"
    LEFT JOIN "ProductGroup" parent ON parent."Id" = pg."ParentId"
    WHERE immutable_unaccent(LOWER(pg."Label")) % n.q_unaccent
),
ranked AS (
    SELECT
        c."Sku",
        c."Ean",
        c."Name",
        c."GroupLabel",
        c."SubGroupLabel",
        c.score,
        ROW_NUMBER() OVER (PARTITION BY c."Sku" ORDER BY c.score DESC, c."Name" ASC) AS rn
    FROM candidates c
)
SELECT
    r."Sku" AS "Sku",
    r."Ean" AS "Ean",
    r."Name" AS "Name",
    r."GroupLabel" AS "Group",
    r."SubGroupLabel" AS "SubGroup"
FROM ranked r
CROSS JOIN normalized n
WHERE rn = 1
ORDER BY r.score DESC, r."Name" ASC
LIMIT n.limit;
""";

        var rows = await connection.QueryAsync<ProductSuggestionItem>(
            new CommandDefinition(
                sql,
                new
                {
                    Query = query,
                    Limit = limit
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToArray();
    }

    private static string BuildRawCodeClause(bool supportsRawCode)
    {
        if (!supportsRawCode)
        {
            return string.Empty;
        }

        return """

    UNION ALL

    SELECT
        p."Sku",
        p."Ean",
        p."Name",
        parent."Label" AS "GroupLabel",
        pg."Label" AS "SubGroupLabel",
        1.0::double precision AS score
    FROM normalized n
    JOIN "Product" p ON p."Code" ILIKE n.q || '%'
    LEFT JOIN "ProductGroup" pg ON pg."Id" = p."GroupId"
    LEFT JOIN "ProductGroup" parent ON parent."Id" = pg."ParentId"
    WHERE p."Code" IS NOT NULL
""";
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
}
