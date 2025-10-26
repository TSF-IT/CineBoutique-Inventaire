using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace CineBoutique.Inventory.Infrastructure.Database.Inventory;

internal static class InventoryDbUtilities
{
    public static async Task EnsureConnectionOpenAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

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

    public static Task<bool> TableExistsAsync(IDbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT EXISTS (
    SELECT 1
    FROM information_schema.tables
    WHERE LOWER(table_name) = LOWER(@TableName)
      AND table_schema = ANY (current_schemas(TRUE))
      AND table_type IN ('BASE TABLE', 'VIEW', 'FOREIGN TABLE', 'LOCAL TEMPORARY', 'GLOBAL TEMPORARY')
);";

        return connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { TableName = tableName }, cancellationToken: cancellationToken));
    }

    public static Task<bool> ColumnExistsAsync(IDbConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE LOWER(table_name) = LOWER(@TableName)
      AND LOWER(column_name) = LOWER(@ColumnName)
      AND table_schema = ANY (current_schemas(TRUE))
);";

        return connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { TableName = tableName, ColumnName = columnName }, cancellationToken: cancellationToken));
    }

    public static Task<bool> ColumnIsNullableAsync(IDbConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT
    CASE
        WHEN COUNT(*) = 0 THEN TRUE
        ELSE BOOL_OR(is_nullable = 'YES')
    END
FROM information_schema.columns
WHERE LOWER(table_name) = LOWER(@TableName)
  AND LOWER(column_name) = LOWER(@ColumnName)
  AND table_schema = ANY (current_schemas(TRUE));";

        return connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { TableName = tableName, ColumnName = columnName }, cancellationToken: cancellationToken));
    }
}
