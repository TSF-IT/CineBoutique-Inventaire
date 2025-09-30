// Modifications : déplacement des endpoints de santé (/health, /ready, /healthz) et du writer JSON dédié.
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

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
