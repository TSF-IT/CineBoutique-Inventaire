using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json; // + JSON writer pour health
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Auth;
using CineBoutique.Inventory.Api.Configuration;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.DependencyInjection;
using CineBoutique.Inventory.Infrastructure.Migrations;
using CineBoutique.Inventory.Infrastructure.Seeding;
using Dapper;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks; // + HealthChecks mapping
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks; // + HealthChecks types
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Npgsql;
using Serilog;

// + HealthCheck DB (tu crées le fichier DatabaseHealthCheck.cs)
using CineBoutique.Inventory.Api.Infrastructure.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = false;
    options.ValidateScopes = false;
});

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

bool disableSerilog = builder.Configuration["DISABLE_SERILOG"]?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (allowedOrigins.Length == 0)
{
    allowedOrigins = new[]
    {
        "http://localhost:5173",
        "http://127.0.0.1:5173",
    };
}

var useSerilog = !disableSerilog;

if (useSerilog)
{
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));
}
else
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
}

builder.Services.Configure<AppSettingsOptions>(builder.Configuration.GetSection("AppSettings"));

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("La chaîne de connexion 'Default' doit être configurée.");
}

builder.Services
    .AddFluentMigratorCore()
    .ConfigureRunner(rb => rb
        .AddPostgres()
        .WithGlobalConnectionString(connectionString)
        .ScanIn(typeof(Program).Assembly, typeof(MigrationsAssemblyMarker).Assembly).For.Migrations())
    .AddLogging(lb => lb.AddFluentMigratorConsole());

builder.Services.Configure<SelectingProcessorAccessorOptions>(options =>
{
    options.ProcessorId = "Postgres";
});

builder.Services
    .AddOptions<ProcessorOptions>()
    .Configure(options =>
    {
        options.Timeout = TimeSpan.FromSeconds(90);
        options.ProviderSwitches = string.Empty;
        options.PreviewOnly = false;
    });

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ProcessorOptions>>().Value);

var authenticationSection = builder.Configuration.GetSection("Authentication");
var authenticationOptions = authenticationSection.Get<AuthenticationOptions>()
    ?? throw new InvalidOperationException("La section de configuration 'Authentication' est manquante.");

if (string.IsNullOrWhiteSpace(authenticationOptions.Secret))
{
    throw new InvalidOperationException("Le secret d'authentification JWT doit être configuré.");
}

if (authenticationOptions.TokenLifetimeMinutes <= 0)
{
    throw new InvalidOperationException("La durée de vie du token JWT doit être supérieure à zéro.");
}

builder.Services.Configure<AuthenticationOptions>(authenticationSection);

builder.Services.AddInventoryInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CinéBoutique Inventory API", Version = "v1" });

    var asmName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, $"{asmName}.xml");
    if (System.IO.File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddControllers();

// --- Health checks (liveness + readiness) ---
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy()) // liveness
    .AddCheck<DatabaseHealthCheck>("db", tags: new[] { "ready" }); // readiness DB

builder.Services.AddScoped<IDbConnection>(sp =>
{
    var factory = sp.GetRequiredService<IDbConnectionFactory>();
    return factory.CreateConnection();
});

// IAuditLogger (API) -> DbAuditLogger écrit déjà dans audit_logs
builder.Services.AddScoped<IAuditLogger, DbAuditLogger>();

// BRIDGE : remplace l'impl par défaut du Domain (DapperAuditLogger) par le pont vers DbAuditLogger
builder.Services.AddScoped<CineBoutique.Inventory.Domain.Auditing.IAuditLogger,
    DomainAuditBridgeLogger>();

builder.Services.AddSingleton<ITokenService, JwtTokenService>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authenticationOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authenticationOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(authenticationOptions.Secret))
        };
    });

builder.Services.AddAuthorization();

const string PublicApiCorsPolicy = "PublicApi";
const string DevCorsPolicy = "AllowDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policyBuilder =>
    {
        policyBuilder
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });

    options.AddPolicy(PublicApiCorsPolicy, policyBuilder =>
    {
        if (allowedOrigins.Length > 0)
        {
            // Pense à ajouter l'IP LAN du poste (ex: http://192.168.1.42:5173) dans AllowedOrigins pour tester depuis l'iPhone.
            policyBuilder.WithOrigins(allowedOrigins).AllowCredentials();
        }
        else
        {
            policyBuilder.SetIsOriginAllowed(_ => true);
        }

        policyBuilder
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .AllowAnyHeader()
            .SetPreflightMaxAge(TimeSpan.FromHours(1));
    });
});

var app = builder.Build();

// --- FORWARDED HEADERS pour proxy (Nginx) ---
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("CI"))
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            var problem = new ProblemDetails
            {
                Title = "An error occurred while processing your request.",
                Status = StatusCodes.Status500InternalServerError,
                Detail = app.Environment.IsDevelopment() ? feature?.Error.ToString() : null
            };

            context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(problem).ConfigureAwait(false);
        });
    });
}

app.UseStatusCodePages();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        return Task.CompletedTask;
    });

    await next().ConfigureAwait(false);
});

var applyMigrations = app.Configuration.GetValue<bool>("APPLY_MIGRATIONS");
var disableMigrations = app.Configuration.GetValue<bool>("DISABLE_MIGRATIONS");
app.Logger.LogInformation("APPLY_MIGRATIONS={Apply} DISABLE_MIGRATIONS={Disable}", applyMigrations, disableMigrations);

if (applyMigrations && !disableMigrations)
{
    const int maxAttempts = 10;
    var attempt = 0;

    while (true)
    {
        try
        {
            attempt++;
            using var scope = app.Services.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();

            var connectionDetails = new NpgsqlConnectionStringBuilder(connectionString);
            app.Logger.LogDebug(
                "ConnectionStrings:Default (host/db)={HostAndDatabase}",
                $"{connectionDetails.Host}/{connectionDetails.Database}");

            var seedOnStartup = app.Configuration.GetValue<bool>("AppSettings:SeedOnStartup");
            app.Logger.LogDebug("AppSettings:SeedOnStartup={SeedOnStartup}", seedOnStartup);

            if (seedOnStartup)
            {
                var seeder = scope.ServiceProvider.GetService<InventoryDataSeeder>();
                if (seeder is not null)
                {
                    await seeder.SeedAsync().ConfigureAwait(false);
                }
            }

            break;
        }
        catch (Exception ex) when (attempt < maxAttempts && ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            app.Logger.LogWarning(ex, "Échec de l'application des migrations (tentative {Attempt}/{Max}). Nouvel essai imminent.", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }
}
else if (applyMigrations)
{
    app.Logger.LogInformation("Migrations skipped because DISABLE_MIGRATIONS is true");
}

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCorsPolicy);
}
else
{
    app.UseCors(PublicApiCorsPolicy);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CinéBoutique Inventory API v1");
        c.RoutePrefix = "swagger";
    });
}

if (useSerilog)
{
    app.UseSerilogRequestLogging();
}

app.UseAuthentication();
app.UseAuthorization();

// -------- Health endpoints (JSON propre) --------
static Task WriteHealthJson(HttpContext ctx, HealthReport report)
{
    ctx.Response.ContentType = "application/json; charset=utf-8";

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

    return ctx.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

// Liveness (ne charge aucun check → 200 si le process répond)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthJson
}).AllowAnonymous();

// Readiness (DB taggée "ready")
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

// Option: compat hérité si tu avais /healthz auparavant (liveness)
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthJson
}).AllowAnonymous();

// ===== ALIAS D'AUTH PIN (pour CI ET proxy) =====
app.MapPost("/auth/pin", HandlePinLoginAsync)
   .WithName("PinLogin")
   .AllowAnonymous();

app.MapPost("/api/auth/pin", HandlePinLoginAsync)
   .WithName("ApiPinLogin")
   .AllowAnonymous();

app.MapControllers();

var diag = app.MapGroup("/api/_diag").WithTags("_diag");

diag.MapGet("/info", (IConfiguration cfg, IWebHostEnvironment env) =>
{
    static string Mask(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

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

// --- SUPPRESSION de l'ancien MapGet("/ready", ...) custom ---
// (remplacé par HealthChecks ci-dessus)

app.MapGet("/api/inventories/summary", async (IDbConnection connection, CancellationToken cancellationToken) =>
{
    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

    const string summarySql = @"SELECT
    (SELECT COUNT(*)::int FROM ""InventorySession"" WHERE ""CompletedAtUtc"" IS NULL) AS ""ActiveSessions"",
    (SELECT COUNT(*)::int FROM ""CountingRun"" WHERE ""CompletedAtUtc"" IS NULL) AS ""OpenRuns"",
    (SELECT COUNT(*)::int FROM ""Conflict"" WHERE ""ResolvedAtUtc"" IS NULL) AS ""Conflicts"",
    (
        SELECT MAX(value) FROM (
            SELECT MAX(""CreatedAtUtc"") AS value FROM ""Audit""
            UNION ALL
            SELECT MAX(""CountedAtUtc"") FROM ""CountLine""
            UNION ALL
            SELECT MAX(""StartedAtUtc"") FROM ""CountingRun""
            UNION ALL
            SELECT MAX(""CompletedAtUtc"") FROM ""CountingRun""
        ) AS activity
    ) AS ""LastActivityUtc"";";

    var summary = await connection
        .QuerySingleAsync<InventorySummaryDto>(new CommandDefinition(summarySql, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    return Results.Ok(summary);
})
.WithName("GetInventorySummary")
.WithTags("Inventories")
.Produces<InventorySummaryDto>(StatusCodes.Status200OK)
.WithOpenApi(op =>
{
    op.Summary = "Récupère un résumé des inventaires en cours.";
    op.Description = "Fournit un aperçu synthétique incluant les comptages en cours, les conflits à résoudre et la dernière activité.";
    return op;
});

app.MapGet("/api/locations", async (int? countType, IDbConnection connection, CancellationToken cancellationToken) =>
{
    if (countType.HasValue && countType is not (1 or 2 or 3))
    {
        return Results.BadRequest(new { message = "Le paramètre countType doit valoir 1, 2 ou 3." });
    }

    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

    const string sql = @"WITH active_runs AS (
    SELECT DISTINCT ON (cr.""LocationId"")
        cr.""LocationId"",
        cr.""Id"" AS ""ActiveRunId"",
        cr.""CountType"" AS ""ActiveCountType"",
        cr.""StartedAtUtc"" AS ""ActiveStartedAtUtc"",
        cr.""OperatorDisplayName"" AS ""BusyBy""
    FROM ""CountingRun"" cr
    WHERE cr.""CompletedAtUtc"" IS NULL
      AND (@CountType IS NULL OR cr.""CountType"" = @CountType)
    ORDER BY cr.""LocationId"", cr.""StartedAtUtc"" DESC
)
SELECT
    l.""Id"",
    l.""Code"",
    l.""Label"",
    (ar.""ActiveRunId"" IS NOT NULL) AS ""IsBusy"",
    ar.""BusyBy"",
    CASE
        WHEN ar.""ActiveRunId"" IS NULL THEN NULL
        WHEN ar.""ActiveRunId""::text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' THEN ar.""ActiveRunId""
        ELSE NULL
    END AS ""ActiveRunId"",
    ar.""ActiveCountType"",
    ar.""ActiveStartedAtUtc""
FROM ""Location"" l
LEFT JOIN active_runs ar ON l.""Id"" = ar.""LocationId""
ORDER BY l.""Code"" ASC;";

    var locations = (await connection
        .QueryAsync<LocationListItemDto>(new CommandDefinition(sql, new { CountType = countType }, cancellationToken: cancellationToken))
        .ConfigureAwait(false)).ToList();

    if (locations.Count == 0)
    {
        return Results.Ok(locations);
    }

    var locationIds = locations.Select(location => location.Id).ToArray();

    const string openRunsSql = @"SELECT
    cr.""LocationId"",
    cr.""CountType"",
    cr.""Id"" AS ""RunId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc"",
    cr.""OperatorDisplayName""
FROM ""CountingRun"" cr
WHERE cr.""CompletedAtUtc"" IS NULL
  AND cr.""LocationId"" = ANY(@LocationIds)
ORDER BY cr.""LocationId"", cr.""CountType"", cr.""StartedAtUtc"" DESC;";

    const string completedRunsSql = @"SELECT DISTINCT ON (cr.""LocationId"", cr.""CountType"")
    cr.""LocationId"",
    cr.""CountType"",
    cr.""Id"" AS ""RunId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc"",
    cr.""OperatorDisplayName""
FROM ""CountingRun"" cr
WHERE cr.""CompletedAtUtc"" IS NOT NULL
  AND cr.""LocationId"" = ANY(@LocationIds)
ORDER BY cr.""LocationId"", cr.""CountType"", cr.""CompletedAtUtc"" DESC;";

    var openRuns = await connection
        .QueryAsync<LocationCountStatusRow>(new CommandDefinition(openRunsSql, new { LocationIds = locationIds }, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    var completedRuns = await connection
        .QueryAsync<LocationCountStatusRow>(new CommandDefinition(completedRunsSql, new { LocationIds = locationIds }, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    var openLookup = openRuns.ToLookup(row => (row.LocationId, row.CountType));
    var completedLookup = completedRuns.ToLookup(row => (row.LocationId, row.CountType));

    static IEnumerable<short> DiscoverCountTypes(IEnumerable<LocationCountStatusRow> runs)
        => runs
            .Select(row => row.CountType)
            .Where(countTypeValue => countTypeValue > 0)
            .Distinct();

    var discoveredCountTypes = DiscoverCountTypes(openRuns).Concat(DiscoverCountTypes(completedRuns));

    var defaultCountTypes = new short[] { 1, 2 };

    if (countType is { } requested)
    {
        defaultCountTypes = defaultCountTypes.Concat(new[] { (short)requested }).ToArray();
    }

    var targetCountTypes = defaultCountTypes
        .Concat(discoveredCountTypes)
        .Distinct()
        .OrderBy(value => value)
        .ToArray();

    foreach (var location in locations)
    {
        var statuses = new List<LocationCountStatusDto>(targetCountTypes.Length);

        foreach (var type in targetCountTypes)
        {
            var status = new LocationCountStatusDto
            {
                CountType = type
            };

            var open = openLookup[(location.Id, type)].FirstOrDefault();
            if (open is not null)
            {
                status.Status = LocationCountStatus.InProgress;
                status.RunId = SanitizeRunId(open.RunId);
                status.OperatorDisplayName = open.OperatorDisplayName;
                status.StartedAtUtc = open.StartedAtUtc;
            }
            else
            {
                var completed = completedLookup[(location.Id, type)].FirstOrDefault();
                if (completed is not null)
                {
                    status.Status = LocationCountStatus.Completed;
                    status.RunId = SanitizeRunId(completed.RunId);
                    status.OperatorDisplayName = completed.OperatorDisplayName;
                    status.StartedAtUtc = completed.StartedAtUtc;
                    status.CompletedAtUtc = completed.CompletedAtUtc;
                }
            }

            statuses.Add(status);
        }

        location.CountStatuses = statuses;

        LocationCountStatusDto? active = null;
        if (countType is { } requestedType)
        {
            active = statuses.FirstOrDefault(
                state => state.Status == LocationCountStatus.InProgress && state.CountType == requestedType);
        }

        if (active is null && countType is null)
        {
            active = statuses.FirstOrDefault(state => state.Status == LocationCountStatus.InProgress);
        }

        location.IsBusy = active is not null;
        location.ActiveRunId = active?.RunId;
        location.ActiveCountType = active?.CountType;
        location.ActiveStartedAtUtc = active?.StartedAtUtc;
        location.BusyBy = active?.OperatorDisplayName;
    }

    return Results.Ok(locations);
})
.WithName("GetLocations")
.WithTags("Locations")
.Produces<IEnumerable<LocationListItemDto>>(StatusCodes.Status200OK)
.WithOpenApi(op =>
{
    op.Summary = "Liste les emplacements (locations)";
    op.Description = "Retourne les métadonnées et l'état d'occupation des locations, filtré par type de comptage optionnel.";
    op.Parameters ??= new List<OpenApiParameter>();
    if (!op.Parameters.Any(parameter => string.Equals(parameter.Name, "countType", StringComparison.OrdinalIgnoreCase)))
    {
        op.Parameters.Add(new OpenApiParameter
        {
            Name = "countType",
            In = ParameterLocation.Query,
            Required = false,
            Description = "Type de comptage ciblé (1 pour premier passage, 2 pour second, 3 pour contrôle).",
            Schema = new OpenApiSchema { Type = "integer", Minimum = 1, Maximum = 3 }
        });
    }
    return op;
});

app.MapPost("/api/inventories/{locationId:guid}/restart", async (
    Guid locationId,
    int countType,
    IDbConnection connection,
    IAuditLogger auditLogger,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (countType is not (1 or 2 or 3))
    {
        return Results.BadRequest(new { message = "Le paramètre countType doit valoir 1, 2 ou 3." });
    }

    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

    const string sql = @"UPDATE ""CountingRun""
SET ""CompletedAtUtc"" = @NowUtc
WHERE ""LocationId"" = @LocationId
  AND ""CompletedAtUtc"" IS NULL
  AND ""CountType"" = @CountType;";

    var now = DateTimeOffset.UtcNow;
    var affected = await connection
        .ExecuteAsync(new CommandDefinition(sql, new { LocationId = locationId, CountType = countType, NowUtc = now }, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    var locationInfo = await connection
        .QuerySingleOrDefaultAsync<LocationMetadata>(
            new CommandDefinition(
                "SELECT \"Code\" AS Code, \"Label\" AS Label FROM \"Location\" WHERE \"Id\" = @LocationId LIMIT 1",
                new { LocationId = locationId },
                cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    var userName = GetAuthenticatedUserName(httpContext);
    var actor = FormatActorLabel(userName);
    var timestamp = FormatTimestamp(now);
    var zoneDescription = locationInfo is not null
        ? $"la zone {locationInfo.Code} – {locationInfo.Label}"
        : $"la zone {locationId}";
    var countDescription = DescribeCountType(countType);
    var resultDetails = affected > 0 ? "et clôturé les comptages actifs" : "mais aucun comptage actif n'était ouvert";
    var message = $"{actor} a relancé {zoneDescription} pour un {countDescription} le {timestamp} UTC {resultDetails}.";

    await auditLogger.LogAsync(message, userName, "inventories.restart").ConfigureAwait(false);

    return Results.NoContent();
})
.WithName("RestartInventoryForLocation")
.WithTags("Inventories")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status400BadRequest)
.WithOpenApi(op =>
{
    op.Summary = "Force la clôture des comptages actifs pour une zone et un type donnés.";
    op.Description = "Permet de terminer les runs ouverts sur une zone pour relancer un nouveau comptage.";
    return op;
});

app.MapGet("/products/{code}", async (
    string code,
    IDbConnection connection,
    IAuditLogger auditLogger,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(code))
    {
        var nowInvalid = DateTimeOffset.UtcNow;
        var invalidUser = GetAuthenticatedUserName(httpContext);
        var invalidActor = FormatActorLabel(invalidUser);
        var invalidTimestamp = FormatTimestamp(nowInvalid);
        var invalidMessage = $"{invalidActor} a tenté de scanner un code produit vide le {invalidTimestamp} UTC.";
        await auditLogger.LogAsync(invalidMessage, invalidUser, "products.scan.invalid").ConfigureAwait(false);
        return Results.BadRequest(new { message = "Le code produit est requis." });
    }

    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

    var sanitizedCode = code.Trim();
    var isPotentialEan = sanitizedCode.All(char.IsDigit) && (sanitizedCode.Length == 8 || sanitizedCode.Length == 13);

    ProductDto? product = null;
    if (isPotentialEan)
    {
        product = await connection.QuerySingleOrDefaultAsync<ProductDto>(new CommandDefinition("SELECT \"Id\", \"Sku\", \"Name\", \"Ean\" FROM \"Product\" WHERE \"Ean\" = @Code LIMIT 1", new { Code = sanitizedCode }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    if (product is null)
    {
        product = await connection.QuerySingleOrDefaultAsync<ProductDto>(new CommandDefinition("SELECT \"Id\", \"Sku\", \"Name\", \"Ean\" FROM \"Product\" WHERE LOWER(\"Sku\") = LOWER(@Code) LIMIT 1", new { Code = sanitizedCode }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    var now = DateTimeOffset.UtcNow;
    var userName = GetAuthenticatedUserName(httpContext);
    var actor = FormatActorLabel(userName);
    var timestamp = FormatTimestamp(now);

    if (product is not null)
    {
        var productLabel = product.Name;
        var skuLabel = string.IsNullOrWhiteSpace(product.Sku) ? "non renseigné" : product.Sku;
        var eanLabel = string.IsNullOrWhiteSpace(product.Ean) ? "non renseigné" : product.Ean;
        var successMessage = $"{actor} a scanné le code {sanitizedCode} et a identifié le produit \"{productLabel}\" (SKU {skuLabel}, EAN {eanLabel}) le {timestamp} UTC.";
        await auditLogger.LogAsync(successMessage, userName, "products.scan.success").ConfigureAwait(false);
        return Results.Ok(product);
    }

    var notFoundMessage = $"{actor} a scanné le code {sanitizedCode} sans correspondance produit le {timestamp} UTC.";
    await auditLogger.LogAsync(notFoundMessage, userName, "products.scan.not_found").ConfigureAwait(false);

    return Results.NotFound();
});

await app.RunAsync().ConfigureAwait(false);

// =================== Handlers & helpers ===================

// Handler partagé pour /auth/pin et /api/auth/pin
static async Task<IResult> HandlePinLoginAsync(
    PinAuthenticationRequest request,
    ITokenService tokenService,
    IOptions<AuthenticationOptions> options,
    IAuditLogger auditLogger)
{
    if (request is null || string.IsNullOrWhiteSpace(request.Pin))
    {
        var timestamp = FormatTimestamp(DateTimeOffset.UtcNow);
        await auditLogger.LogAsync($"Tentative de connexion admin rejetée : code PIN absent le {timestamp} UTC.", null, "auth.pin.failure").ConfigureAwait(false);
        return Results.BadRequest(new { message = "Le code PIN est requis." });
    }

    var providedPin = request.Pin.Trim();
    var user = options.Value.Users.FirstOrDefault(u => string.Equals(u.Pin, providedPin, StringComparison.Ordinal));
    if (user is null)
    {
        var timestamp = FormatTimestamp(DateTimeOffset.UtcNow);
        await auditLogger.LogAsync($"Tentative de connexion admin refusée (PIN inconnu) le {timestamp} UTC.", null, "auth.pin.failure").ConfigureAwait(false);
        return Results.Unauthorized();
    }

    var tokenResult = tokenService.GenerateToken(user.Name);
    var response = new PinAuthenticationResponse(user.Name, tokenResult.AccessToken, tokenResult.ExpiresAtUtc);

    var successTimestamp = FormatTimestamp(DateTimeOffset.UtcNow);
    var actor = FormatActorLabel(user.Name);
    await auditLogger.LogAsync($"{actor} s'est connecté avec succès le {successTimestamp} UTC.", user.Name, "auth.pin.success").ConfigureAwait(false);

    return Results.Ok(response);
}

static async Task EnsureConnectionOpenAsync(IDbConnection connection, CancellationToken cancellationToken)
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

static string? GetAuthenticatedUserName(HttpContext context)
{
    if (context?.User?.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name })
    {
        return name.Trim();
    }

    var fallback = context?.User?.Identity?.Name;
    return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
}

static string FormatActorLabel(string? userName)
{
    return string.IsNullOrWhiteSpace(userName)
        ? "Un utilisateur non authentifié"
        : $"L'utilisateur {userName.Trim()}";
}

static string FormatTimestamp(DateTimeOffset timestamp)
{
    return timestamp.ToUniversalTime().ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture);
}

static string DescribeCountType(int countType)
{
    return countType switch
    {
        1 => "premier passage",
        2 => "second passage",
        3 => "contrôle",
        _ => $"type {countType}"
    };
}

static Guid? SanitizeRunId(Guid? runId)
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

    var versionChar = buffer[14];
    if (versionChar is < '1' or > '8')
    {
        return null;
    }

    var variantChar = char.ToLowerInvariant(buffer[19]);
    return variantChar is '8' or '9' or 'a' or 'b'
        ? runId
        : null;
}

file sealed record LocationCountStatusRow(
    Guid LocationId,
    short CountType,
    Guid? RunId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? OperatorDisplayName);

public partial class Program
{
}
