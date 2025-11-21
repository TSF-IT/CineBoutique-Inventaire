using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Dapper;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/health", async (IDbConnection connection, CancellationToken cancellationToken) =>
        {
            var usersCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM \"ShopUser\"",
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var runsWithoutOwner = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM \"CountingRun\" WHERE \"OwnerUserId\" IS NULL",
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return Results.Ok(new
            {
                app = "inventory-api",
                db = "inventory",
                users = usersCount,
                runsWithoutOwner
            });
        }).AllowAnonymous();

        app.MapGet("/api/ping", () => Results.Ok(new { message = "pong" }))
            .AllowAnonymous()
            .WithTags("Diagnostics")
            .WithName("Ping");

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthJson
        }).AllowAnonymous();

        app.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready"),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = WriteHealthJson
        }).AllowAnonymous();

        app.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthJson
        }).AllowAnonymous();

        return app;
    }

    private static Task WriteHealthJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            results = report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    status = kvp.Value.Status.ToString(),
                    durationMs = kvp.Value.Duration.TotalMilliseconds,
                    error = kvp.Value.Exception?.Message
                })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
