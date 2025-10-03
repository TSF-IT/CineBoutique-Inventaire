// Modifications : centralisation des utilitaires partagés entre les endpoints minimal API.
using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Infrastructure.Middleware;
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
    FROM information_schema.tables
    WHERE LOWER(table_name) = LOWER(@TableName)
      AND table_schema = ANY (current_schemas(TRUE))
      AND table_type IN ('BASE TABLE', 'VIEW', 'FOREIGN TABLE', 'LOCAL TEMPORARY', 'GLOBAL TEMPORARY')
);";

        return await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { TableName = tableName }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public static async Task<bool> ColumnExistsAsync(IDbConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE LOWER(table_name) = LOWER(@TableName)
      AND LOWER(column_name) = LOWER(@ColumnName)
      AND table_schema = ANY (current_schemas(TRUE))
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
        if (context is null)
        {
            return null;
        }

        var identity = context.User?.Identity;
        if (identity is null)
        {
            return null;
        }

        var name = identity.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public static SoftOperatorMiddleware.OperatorContext? GetOperatorContext(HttpContext context)
    {
        if (context is null)
        {
            return null;
        }

        if (!context.Items.TryGetValue(SoftOperatorMiddleware.OperatorContextItemKey, out var value))
        {
            return null;
        }

        return value as SoftOperatorMiddleware.OperatorContext;
    }

    public static string? GetAuditActor(HttpContext context)
    {
        var operatorContext = GetOperatorContext(context);
        var userName = GetAuthenticatedUserName(context);

        return ComposeAuditActor(userName, operatorContext);
    }

    public static string FormatActorLabel(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var operatorContext = GetOperatorContext(context);
        var userName = GetAuthenticatedUserName(context);

        return FormatActorLabel(userName, operatorContext);
    }

    public static string FormatActorLabel(string? userName) =>
        FormatActorLabel(userName, operatorContext: null);

    public static string FormatActorLabel(string? userName, SoftOperatorMiddleware.OperatorContext? operatorContext)
    {
        if (operatorContext is not null)
        {
            var displayName = string.IsNullOrWhiteSpace(operatorContext.OperatorName)
                ? operatorContext.OperatorId.ToString("D")
                : operatorContext.OperatorName.Trim();

            return $"L'opérateur {displayName}";
        }

        return string.IsNullOrWhiteSpace(userName)
            ? "Un utilisateur non authentifié"
            : $"L'utilisateur {userName.Trim()}";
    }

    internal static string? ComposeAuditActor(string? userName, SoftOperatorMiddleware.OperatorContext? operatorContext)
    {
        var hasUser = !string.IsNullOrWhiteSpace(userName);
        var operatorActor = operatorContext is null
            ? null
            : BuildOperatorActorLabel(operatorContext);

        if (!hasUser && string.IsNullOrWhiteSpace(operatorActor))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(operatorActor) && hasUser)
        {
            return $"{userName!.Trim()} | {operatorActor}";
        }

        return !string.IsNullOrWhiteSpace(operatorActor)
            ? operatorActor
            : userName?.Trim();
    }

    private static string? BuildOperatorActorLabel(SoftOperatorMiddleware.OperatorContext context)
    {
        var operatorLabel = context.OperatorId.ToString("D");

        if (!string.IsNullOrWhiteSpace(context.OperatorName))
        {
            operatorLabel = $"{context.OperatorName.Trim()} ({context.OperatorId:D})";
        }

        if (!string.IsNullOrWhiteSpace(context.SessionId))
        {
            operatorLabel = $"{operatorLabel} session:{context.SessionId.Trim()}";
        }

        return $"operator:{operatorLabel}";
    }

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
