// Modifications : déplacement des endpoints de santé (/health, /ready, /healthz) et du writer JSON dédié.
using System;
using System.Data;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Dapper;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/health", () => Results.Text("Healthy"))
           .WithName("Liveness")
           .AllowAnonymous();

        app.MapGet("/healthz", () => Results.Text("Healthy"))
           .WithName("LivenessZ")
           .AllowAnonymous();

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

        app.MapGet("/ready", async (IDbConnection connection, CancellationToken cancellationToken) =>
        {
            try
            {
                await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT 1",
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                return Results.Ok("Ready");
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    status = "not-ready",
                    error = ex.Message
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("Readiness").AllowAnonymous();

        return app;
    }
}
