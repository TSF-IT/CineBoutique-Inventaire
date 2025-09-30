// Modifications : extraction du groupe d'endpoints /api/_diag depuis Program.cs pour clarifier le bootstrap.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var diag = app.MapGroup("/api/_diag");

        diag.MapGet("/info", (IConfiguration cfg, IWebHostEnvironment env) =>
        {
            static string Mask(string value)
            {
                var masked = Regex.Replace(value, @"(Username|User Id|User|UID)\s*=\s*[^;]+", "$1=***", RegexOptions.IgnoreCase);
                masked = Regex.Replace(masked, @"(Password|PWD)\s*=\s*[^;]+", "$1=***", RegexOptions.IgnoreCase);
                return masked;
            }

            var assembly = typeof(Program).Assembly.GetName();
            var connectionString = cfg.GetConnectionString("Default") ?? string.Empty;

            return Results.Ok(new
            {
                env = env.EnvironmentName,
                version = assembly.Version?.ToString(),
                assembly = assembly.Name,
                dbProvider = "Npgsql",
                connectionStringMasked = Mask(connectionString)
            });
        }).WithName("DiagInfo");

#pragma warning disable CA1031
        diag.MapGet("/ping-db", async (IConfiguration cfg) =>
        {
            var cs = cfg.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(cs))
            {
                return Results.Problem("ConnectionStrings:Default is missing.", statusCode: StatusCodes.Status500InternalServerError);
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await using var conn = new NpgsqlConnection(cs);
                await conn.OpenAsync().ConfigureAwait(false);
                await using var cmd = new NpgsqlCommand("SELECT 1", conn);
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                stopwatch.Stop();
                return Results.Ok(new { ok = true, result, elapsedMs = stopwatch.ElapsedMilliseconds });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return Results.Problem(
                    $"DB ping failed: {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError,
                    extensions: new Dictionary<string, object?>
                    {
                        ["elapsedMs"] = stopwatch.ElapsedMilliseconds
                    });
            }
        }).WithName("DiagPingDb");
#pragma warning restore CA1031

        return app;
    }
}
