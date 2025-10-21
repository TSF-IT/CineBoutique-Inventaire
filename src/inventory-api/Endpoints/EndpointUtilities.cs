// Modifications : centralisation des utilitaires partagés entre les endpoints minimal API.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Infrastructure.Middleware;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using FluentValidation.Results;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public static async Task<bool> ColumnIsNullableAsync(IDbConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
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

    public static IResult ValidationProblem(ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var errors = validationResult.Errors
            .GroupBy(error => string.IsNullOrWhiteSpace(error.PropertyName) ? string.Empty : error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct().ToArray(),
                StringComparer.Ordinal);

        if (validationResult.IsValid)
        {
            errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        }

        var problemDetails = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Requête invalide",
            Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1"
        };

        return Results.Json(
            problemDetails,
            ValidationProblemSerializerOptions,
            contentType: "application/problem+json",
            statusCode: problemDetails.Status ?? StatusCodes.Status400BadRequest);
    }

    private static readonly JsonSerializerOptions ValidationProblemSerializerOptions = CreateValidationProblemSerializerOptions();

    private static JsonSerializerOptions CreateValidationProblemSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DictionaryKeyPolicy = null
        };

        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            options.Converters.Add(new JsonStringEnumConverter());
        }

        return options;
    }

    public static IResult Problem(string title, string detail, int statusCode, IDictionary<string, object?>? extensions = null) =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: extensions);

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

    public static OperatorContext? GetOperatorContext(HttpContext context)
    {
        if (context is null)
        {
            return null;
        }

        if (!context.Items.TryGetValue(SoftOperatorMiddleware.OperatorContextItemKey, out var value))
        {
            return null;
        }

        return value as OperatorContext;
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

    public static string FormatActorLabel(string? userName, OperatorContext? operatorContext)
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

    internal static string? ComposeAuditActor(string? userName, OperatorContext? operatorContext)
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

    private static string? BuildOperatorActorLabel(OperatorContext context)
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
