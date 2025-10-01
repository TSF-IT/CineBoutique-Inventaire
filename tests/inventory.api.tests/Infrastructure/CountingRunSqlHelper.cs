using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

internal static class CountingRunSqlHelper
{
    private const string TableName = "CountingRun";
    private const string OperatorColumn = "OperatorDisplayName";

    public static async Task<bool> HasOperatorDisplayNameAsync(
        IDbConnection connection,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT EXISTS (
    SELECT 1
    FROM pg_catalog.pg_attribute a
    WHERE a.attrelid = (
        SELECT c.oid
        FROM pg_catalog.pg_class c
        WHERE c.relkind IN ('r', 'p', 'v', 'm', 'f')
          AND LOWER(c.relname) = LOWER(@TableName)
          AND pg_catalog.pg_table_is_visible(c.oid)
        LIMIT 1
    )
      AND LOWER(a.attname) = LOWER(@ColumnName)
      AND a.attnum > 0
      AND NOT a.attisdropped
);";

        return await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        TableName,
                        ColumnName = OperatorColumn
                    },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public static async Task InsertAsync(
        IDbConnection connection,
        CountingRunInsert insert,
        CancellationToken cancellationToken = default)
    {
        var hasOperatorColumn = await HasOperatorDisplayNameAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        var columnList = "\"Id\", \"InventorySessionId\", \"LocationId\", \"StartedAtUtc\", \"CountType\"";
        var valuesList = "@Id, @InventorySessionId, @LocationId, @StartedAtUtc, @CountType";

        if (insert.CompletedAtUtc.HasValue)
        {
            columnList += ", \"CompletedAtUtc\"";
            valuesList += ", @CompletedAtUtc";
        }

        if (hasOperatorColumn)
        {
            columnList += ", \"OperatorDisplayName\"";
            valuesList += ", @OperatorDisplayName";
        }

        var sql = $"INSERT INTO \"{TableName}\" ({columnList}) VALUES ({valuesList});";

        await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        insert.Id,
                        InventorySessionId = insert.InventorySessionId,
                        insert.LocationId,
                        insert.StartedAtUtc,
                        insert.CompletedAtUtc,
                        insert.CountType,
                        OperatorDisplayName = insert.OperatorDisplayName
                    },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}

internal readonly record struct CountingRunInsert(
    Guid Id,
    Guid InventorySessionId,
    Guid LocationId,
    int CountType,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? OperatorDisplayName);
