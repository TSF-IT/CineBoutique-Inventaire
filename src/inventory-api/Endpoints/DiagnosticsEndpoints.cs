using System.Diagnostics;
using System.Text.RegularExpressions;
using Npgsql;

namespace CineBoutique.Inventory.Api.Endpoints;

internal static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var env = app.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var diag = app.MapGroup("/api/_diag");

        if (env.IsDevelopment())
            diag.AllowAnonymous();
        else
            diag.RequireAuthorization("Admin");

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
                return Results.Problem("ConnectionStrings:Default is missing.", statusCode: StatusCodes.Status500InternalServerError);

            var stopwatch = Stopwatch.StartNew();
            NpgsqlConnection? conn = null;
            NpgsqlCommand? cmd = null;
            try
            {
                conn = new NpgsqlConnection(cs);
                await conn.OpenAsync().ConfigureAwait(false);
                cmd = new NpgsqlCommand("SELECT 1", conn);
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
            finally
            {
                if (cmd is not null)
                    await cmd.DisposeAsync().ConfigureAwait(false);

                if (conn is not null)
                    await conn.DisposeAsync().ConfigureAwait(false);
            }
        }).WithName("DiagPingDb");
#pragma warning restore CA1031

        return app;
    }
}
