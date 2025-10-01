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
        const string sql = @"SELECT 1
FROM information_schema.columns
WHERE table_schema = ANY (current_schemas(false))
  AND LOWER(table_name) = LOWER(@TableName)
  AND LOWER(column_name) = LOWER(@ColumnName)
LIMIT 1;";

        var result = await connection.ExecuteScalarAsync<int?>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        TableName,
                        ColumnName = OperatorColumn
                    },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return result.HasValue;
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
