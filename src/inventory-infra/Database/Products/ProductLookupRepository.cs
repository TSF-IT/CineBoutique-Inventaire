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
