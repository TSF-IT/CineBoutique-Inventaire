// Modifications : centralisation des utilitaires partagés entre les endpoints minimal API.
using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Http;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class EndpointUtilities
{
    public static async Task EnsureConnectionOpenAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
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

    public static async Task<bool> TableExistsAsync(IDbConnection connection, string tableName, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT EXISTS (
    SELECT 1
    FROM pg_catalog.pg_class c
    WHERE c.relkind IN ('r', 'p', 'v', 'm', 'f')
      AND LOWER(c.relname) = LOWER(@TableName)
      AND pg_catalog.pg_table_is_visible(c.oid)
);";

        return await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { TableName = tableName }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public static async Task<bool> ColumnExistsAsync(IDbConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
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
                new CommandDefinition(sql, new { TableName = tableName, ColumnName = columnName }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public static Guid? SanitizeRunId(Guid? runId)
    {
        if (runId is null)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[36];
        if (!runId.Value.TryFormat(buffer, out var written, "D") || written != 36)
        {
            return null;
        }

        var versionChar = char.ToLowerInvariant(buffer[14]);
        if (versionChar is < '1' or > '8')
        {
            return null;
        }

        return runId;
    }

    public static string? GetAuthenticatedUserName(HttpContext context)
    {
        if (context?.User?.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name })
        {
            return name.Trim();
        }

        var fallback = context?.User?.Identity?.Name;
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
    }

    public static string FormatActorLabel(string? userName) =>
        string.IsNullOrWhiteSpace(userName)
            ? "Un utilisateur non authentifié"
            : $"L'utilisateur {userName.Trim()}";

    public static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture);

    public static string DescribeCountType(int countType) => countType switch
    {
        1 => "premier passage",
        2 => "second passage",
        3 => "contrôle",
        _ => $"type {countType}"
    };
}
