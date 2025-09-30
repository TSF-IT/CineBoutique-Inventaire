using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
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
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Npgsql;
using Serilog;
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

var disableSerilog = builder.Configuration["DISABLE_SERILOG"]?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

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
    .AddCheck("self", () => HealthCheckResult.Healthy())
    .AddCheck<DatabaseHealthCheck>("db", tags: new[] { "ready" });

builder.Services.AddScoped<IDbConnection>(sp =>
{
    var factory = sp.GetRequiredService<IDbConnectionFactory>();
    return factory.CreateConnection();
});

// IAuditLogger (API) -> DbAuditLogger écrit déjà dans audit_logs
builder.Services.AddScoped<IAuditLogger, DbAuditLogger>();

// BRIDGE : remplace l'impl du Domain par le pont vers DbAuditLogger
builder.Services.AddScoped<CineBoutique.Inventory.Domain.Auditing.IAuditLogger, DomainAuditBridgeLogger>();

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
            // Ajouter l’IP LAN si besoin (ex: http://192.168.1.42:5173)
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
                Title = "Une erreur est survenue lors du traitement de votre requête.",
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

// Liveness
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthJson
}).AllowAnonymous();

// Readiness (DB tag "ready")
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

// Compat
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthJson
}).AllowAnonymous();

// ===== ALIAS D'AUTH PIN =====
app.MapPost("/auth/pin", HandlePinLoginAsync).WithName("PinLogin").AllowAnonymous();
app.MapPost("/api/auth/pin", HandlePinLoginAsync).WithName("ApiPinLogin").AllowAnonymous();

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

// --- Résumé inventaires ---
app.MapGet("/api/inventories/summary", async (IDbConnection connection, CancellationToken cancellationToken) =>
{
    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

    var activitySources = new List<string>
    {
        "SELECT MAX(\"CountedAtUtc\") AS value FROM \"CountLine\"",
        "SELECT MAX(\"StartedAtUtc\") FROM \"CountingRun\"",
        "SELECT MAX(\"CompletedAtUtc\") FROM \"CountingRun\""
    };

    if (await TableExistsAsync(connection, "Audit", cancellationToken).ConfigureAwait(false))
    {
        activitySources.Insert(0, "SELECT MAX(\"CreatedAtUtc\") AS value FROM \"Audit\"");
    }

    var activityUnion = string.Join("\n            UNION ALL\n            ", activitySources);

    var summarySql = $@"SELECT
    (SELECT COUNT(*)::int FROM ""InventorySession"" WHERE ""CompletedAtUtc"" IS NULL) AS ""ActiveSessions"",
    (SELECT COUNT(*)::int FROM ""CountingRun""   WHERE ""CompletedAtUtc"" IS NULL) AS ""OpenRuns"",
    (SELECT COUNT(*)::int FROM ""Conflict""      WHERE ""ResolvedAtUtc""   IS NULL) AS ""Conflicts"",
    (
        SELECT MAX(value) FROM (
            {activityUnion}
        ) AS activity
    ) AS ""LastActivityUtc"";";

    var summary = await connection
        .QuerySingleAsync<InventorySummaryDto>(new CommandDefinition(summarySql, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    var hasOperatorDisplayNameColumn = await ColumnExistsAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken).ConfigureAwait(false);

    var operatorDisplayNameProjection = hasOperatorDisplayNameColumn
        ? "cr.\"OperatorDisplayName\""
        : "NULL::text";

    var openRunsDetailsSql = $@"SELECT
    cr.""Id""          AS ""RunId"",
    cr.""LocationId"",
    l.""Code""         AS ""LocationCode"",
    l.""Label""        AS ""LocationLabel"",
    cr.""CountType"",
    {operatorDisplayNameProjection} AS ""OperatorDisplayName"",
    cr.""StartedAtUtc""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
WHERE cr.""CompletedAtUtc"" IS NULL
ORDER BY cr.""StartedAtUtc"" DESC;";

    var openRunRows = (await connection
        .QueryAsync<OpenRunSummaryRow>(new CommandDefinition(openRunsDetailsSql, cancellationToken: cancellationToken))
        .ConfigureAwait(false)).ToList();

    var conflictsDetailsSql = $@"SELECT
    c.""Id""          AS ""ConflictId"",
    c.""CountLineId"",
    cl.""CountingRunId"",
    cr.""LocationId"",
    l.""Code""        AS ""LocationCode"",
    l.""Label""       AS ""LocationLabel"",
    cr.""CountType"",
    {operatorDisplayNameProjection} AS ""OperatorDisplayName"",
    c.""CreatedAtUtc""
FROM ""Conflict"" c
JOIN ""CountLine""  cl ON cl.""Id"" = c.""CountLineId""
JOIN ""CountingRun"" cr ON cr.""Id"" = cl.""CountingRunId""
JOIN ""Location""   l  ON l.""Id"" = cr.""LocationId""
WHERE c.""ResolvedAtUtc"" IS NULL
ORDER BY c.""CreatedAtUtc"" DESC;";

    var conflictRows = (await connection
        .QueryAsync<ConflictSummaryRow>(new CommandDefinition(conflictsDetailsSql, cancellationToken: cancellationToken))
        .ConfigureAwait(false)).ToList();

    summary.OpenRunDetails = openRunRows
        .Select(row => new OpenRunSummaryDto
        {
            RunId = row.RunId,
            LocationId = row.LocationId,
            LocationCode = row.LocationCode,
            LocationLabel = row.LocationLabel,
            CountType = row.CountType,
            OperatorDisplayName = row.OperatorDisplayName,
            // StartedAtUtc est non-nullable ici -> utiliser la surcharge non-nullable
            StartedAtUtc = TimeUtil.ToUtcOffset(row.StartedAtUtc),
        })
        .ToArray();

    summary.ConflictDetails = conflictRows
        .Select(row => new ConflictSummaryDto
        {
            ConflictId = row.ConflictId,
            CountLineId = row.CountLineId,
            CountingRunId = row.CountingRunId,
            LocationId = row.LocationId,
            LocationCode = row.LocationCode,
            LocationLabel = row.LocationLabel,
            CountType = row.CountType,
            OperatorDisplayName = row.OperatorDisplayName,
            // CreatedAtUtc est non-nullable
            CreatedAtUtc = TimeUtil.ToUtcOffset(row.CreatedAtUtc),
        })
        .ToArray();

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

// --- Locations + statuts ---
app.MapGet("/api/locations", async (int? countType, IDbConnection connection, CancellationToken cancellationToken) =>
{
    if (countType.HasValue && countType is not (1 or 2 or 3))
    {
        return Results.BadRequest(new { message = "Le paramètre countType doit valoir 1, 2 ou 3." });
    }

    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

    var hasOperatorDisplayNameColumn = await ColumnExistsAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken).ConfigureAwait(false);

    var operatorDisplayNameProjection = hasOperatorDisplayNameColumn
        ? "cr.\"OperatorDisplayName\""
        : "NULL::text";

    var sql = $@"WITH active_runs AS (
    SELECT DISTINCT ON (cr.""LocationId"")
        cr.""LocationId"",
        cr.""Id""          AS ""ActiveRunId"",
        cr.""CountType""   AS ""ActiveCountType"",
        cr.""StartedAtUtc"" AS ""ActiveStartedAtUtc"",
        {operatorDisplayNameProjection} AS ""BusyBy""
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

    var openRunsSql = $@"SELECT
    cr.""LocationId"",
    cr.""CountType"",
    cr.""Id""          AS ""RunId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc"",
    {operatorDisplayNameProjection} AS ""OperatorDisplayName""
FROM ""CountingRun"" cr
WHERE cr.""CompletedAtUtc"" IS NULL
  AND cr.""LocationId"" = ANY(@LocationIds::uuid[])
ORDER BY cr.""LocationId"", cr.""CountType"", cr.""StartedAtUtc"" DESC;";

    var completedRunsSql = $@"SELECT DISTINCT ON (cr.""LocationId"", cr.""CountType"")
    cr.""LocationId"",
    cr.""CountType"",
    cr.""Id""           AS ""RunId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc"",
    {operatorDisplayNameProjection} AS ""OperatorDisplayName""
FROM ""CountingRun"" cr
WHERE cr.""CompletedAtUtc"" IS NOT NULL
  AND cr.""LocationId"" = ANY(@LocationIds::uuid[])
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
                status.StartedAtUtc = TimeUtil.ToUtcOffset(open.StartedAtUtc);
            }
            else
            {
                var completed = completedLookup[(location.Id, type)].FirstOrDefault();
                if (completed is not null)
                {
                    status.Status = LocationCountStatus.Completed;
                    status.RunId = SanitizeRunId(completed.RunId);
                    status.OperatorDisplayName = completed.OperatorDisplayName;
                    status.StartedAtUtc = TimeUtil.ToUtcOffset(completed.StartedAtUtc);
                    status.CompletedAtUtc = TimeUtil.ToUtcOffset(completed.CompletedAtUtc);
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

// --- Terminer un comptage ---
app.MapPost("/api/inventories/{locationId:guid}/complete", async (
    Guid locationId,
    CompleteInventoryRunRequest request,
    IDbConnection connection,
    IAuditLogger auditLogger,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        return Results.BadRequest(new { message = "Le corps de la requête est requis." });
    }

    var countType = request.CountType;
    if (countType is not (1 or 2 or 3))
    {
        return Results.BadRequest(new { message = "Le type de comptage doit valoir 1, 2 ou 3." });
    }

    var operatorName = request.Operator?.Trim();
    if (string.IsNullOrWhiteSpace(operatorName))
    {
        return Results.BadRequest(new { message = "L'opérateur ayant réalisé le comptage est requis." });
    }

    var rawItems = request.Items ?? new List<CompleteInventoryRunItemRequest>();
    if (rawItems.Count == 0)
    {
        return Results.BadRequest(new { message = "Au moins une ligne de comptage doit être fournie." });
    }

    var sanitizedItems = new List<SanitizedCountLine>(rawItems.Count);
    foreach (var item in rawItems)
    {
        var ean = item.Ean?.Trim();
        if (string.IsNullOrWhiteSpace(ean))
        {
            return Results.BadRequest(new { message = "Chaque ligne doit contenir un EAN." });
        }

        if (ean.Length is < 8 or > 13 || !ean.All(char.IsDigit))
        {
            return Results.BadRequest(new { message = $"L'EAN {ean} est invalide. Il doit contenir entre 8 et 13 chiffres." });
        }

        if (item.Quantity <= 0)
        {
            return Results.BadRequest(new { message = $"La quantité pour l'EAN {ean} doit être strictement positive." });
        }

        sanitizedItems.Add(new SanitizedCountLine(ean, item.Quantity, item.IsManual));
    }

    var aggregatedItems = sanitizedItems
        .GroupBy(line => line.Ean, StringComparer.Ordinal)
        .Select(group => new SanitizedCountLine(group.Key, group.Sum(line => line.Quantity), group.Any(line => line.IsManual)))
        .ToList();

    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

    var hasOperatorDisplayNameColumn = await ColumnExistsAsync(
        connection,
        "CountingRun",
        "OperatorDisplayName",
        cancellationToken).ConfigureAwait(false);

    if (connection is not DbConnection dbConnection)
    {
        return Results.Problem(
            "La connexion à la base de données n'est pas compatible avec les transactions.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    await using var transaction = await dbConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

    const string selectLocationSql =
        "SELECT \"Id\", \"Code\", \"Label\" FROM \"Location\" WHERE \"Id\" = @LocationId LIMIT 1;";

    var location = await connection
        .QuerySingleOrDefaultAsync<LocationMetadata>(
            new CommandDefinition(selectLocationSql, new { LocationId = locationId }, transaction, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    if (location is null)
    {
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        return Results.NotFound(new { message = "Zone introuvable pour ce comptage." });
    }

    CountingRunRow? existingRun = null;
    if (request.RunId is Guid runId)
    {
        const string selectRunSql =
            "SELECT \"Id\", \"InventorySessionId\", \"LocationId\", \"CountType\" FROM \"CountingRun\" WHERE \"Id\" = @RunId AND \"LocationId\" = @LocationId LIMIT 1;";

        existingRun = await connection
            .QuerySingleOrDefaultAsync<CountingRunRow>(
                new CommandDefinition(selectRunSql, new { RunId = runId, LocationId = locationId }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (existingRun is not null && existingRun.CountType != countType)
        {
            existingRun = null;
        }
    }

    var now = DateTimeOffset.UtcNow;
    Guid inventorySessionId;
    Guid countingRunId;

    if (existingRun is not null)
    {
        inventorySessionId = existingRun.InventorySessionId;
        countingRunId = existingRun.Id;

        const string deleteLinesSql = "DELETE FROM \"CountLine\" WHERE \"CountingRunId\" = @RunId;";
        await connection
            .ExecuteAsync(new CommandDefinition(deleteLinesSql, new { RunId = countingRunId }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
    else
    {
        inventorySessionId = Guid.NewGuid();
        countingRunId = Guid.NewGuid();

        var sessionName = string.IsNullOrWhiteSpace(location.Code)
            ? $"Inventaire {location.Label}"
            : $"Inventaire {location.Code} – {location.Label}";

        const string insertSessionSql =
            "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\", \"CompletedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc, @CompletedAtUtc);";

        await connection
            .ExecuteAsync(
                new CommandDefinition(
                    insertSessionSql,
                    new
                    {
                        Id = inventorySessionId,
                        Name = sessionName,
                        StartedAtUtc = now,
                        CompletedAtUtc = now
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var insertRunSql = hasOperatorDisplayNameColumn
            ? "INSERT INTO \"CountingRun\" (\"Id\", \"InventorySessionId\", \"LocationId\", \"CountType\", \"StartedAtUtc\", \"CompletedAtUtc\", \"OperatorDisplayName\") " +
              "VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAtUtc, @CompletedAtUtc, @Operator);"
            : "INSERT INTO \"CountingRun\" (\"Id\", \"InventorySessionId\", \"LocationId\", \"CountType\", \"StartedAtUtc\", \"CompletedAtUtc\") " +
              "VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAtUtc, @CompletedAtUtc);";

        object insertRunParameters = hasOperatorDisplayNameColumn
            ? new
            {
                Id = countingRunId,
                SessionId = inventorySessionId,
                LocationId = locationId,
                CountType = countType,
                StartedAtUtc = now,
                CompletedAtUtc = now,
                Operator = operatorName
            }
            : new
            {
                Id = countingRunId,
                SessionId = inventorySessionId,
                LocationId = locationId,
                CountType = countType,
                StartedAtUtc = now,
                CompletedAtUtc = now
            };

        await connection
            .ExecuteAsync(
                new CommandDefinition(
                    insertRunSql,
                    insertRunParameters,
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    const string updateSessionSql =
        "UPDATE \"InventorySession\" SET \"CompletedAtUtc\" = @CompletedAtUtc WHERE \"Id\" = @SessionId;";

    await connection
        .ExecuteAsync(new CommandDefinition(updateSessionSql, new { SessionId = inventorySessionId, CompletedAtUtc = now }, transaction, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    var updateRunSql = hasOperatorDisplayNameColumn
        ? "UPDATE \"CountingRun\" SET \"CountType\" = @CountType, \"CompletedAtUtc\" = @CompletedAtUtc, \"OperatorDisplayName\" = @Operator " +
          "WHERE \"Id\" = @RunId;"
        : "UPDATE \"CountingRun\" SET \"CountType\" = @CountType, \"CompletedAtUtc\" = @CompletedAtUtc " +
          "WHERE \"Id\" = @RunId;";

    object updateRunParameters = hasOperatorDisplayNameColumn
        ? new
        {
            RunId = countingRunId,
            CountType = countType,
            CompletedAtUtc = now,
            Operator = operatorName
        }
        : new
        {
            RunId = countingRunId,
            CountType = countType,
            CompletedAtUtc = now
        };

    await connection
        .ExecuteAsync(
            new CommandDefinition(
                updateRunSql,
                updateRunParameters,
                transaction,
                cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    var requestedEans = aggregatedItems.Select(item => item.Ean).Distinct(StringComparer.Ordinal).ToArray();

    const string selectProductsSql = "SELECT \"Id\", \"Ean\" FROM \"Product\" WHERE \"Ean\" = ANY(@Eans::text[]);";
    var existingProducts = (await connection
            .QueryAsync<ProductLookupRow>(
                new CommandDefinition(selectProductsSql, new { Eans = requestedEans }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false))
        .ToDictionary(row => row.Ean, row => row.Id, StringComparer.Ordinal);

    const string insertProductSql =
        "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc);";

    const string insertLineSql =
        "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@Id, @RunId, @ProductId, @Quantity, @CountedAtUtc);";

    foreach (var item in aggregatedItems)
    {
        if (!existingProducts.TryGetValue(item.Ean, out var productId))
        {
            productId = Guid.NewGuid();
            var sku = BuildUnknownSku(item.Ean);
            var name = $"Produit inconnu EAN {item.Ean}";

            await connection
                .ExecuteAsync(
                    new CommandDefinition(
                        insertProductSql,
                        new
                        {
                            Id = productId,
                            Sku = sku,
                            Name = name,
                            Ean = item.Ean,
                            CreatedAtUtc = now
                        },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            existingProducts[item.Ean] = productId;
        }

        await connection
            .ExecuteAsync(
                new CommandDefinition(
                    insertLineSql,
                    new
                    {
                        Id = Guid.NewGuid(),
                        RunId = countingRunId,
                        ProductId = productId,
                        Quantity = item.Quantity,
                        CountedAtUtc = now
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

    var response = new CompleteInventoryRunResponse
    {
        RunId = countingRunId,
        InventorySessionId = inventorySessionId,
        LocationId = locationId,
        CountType = countType,
        CompletedAtUtc = now,
        ItemsCount = aggregatedItems.Count,
        TotalQuantity = aggregatedItems.Sum(item => item.Quantity),
    };

    var actor = FormatActorLabel(operatorName);
    var timestamp = FormatTimestamp(now);
    var zoneDescription = string.IsNullOrWhiteSpace(location.Code)
        ? location.Label
        : $"{location.Code} – {location.Label}";
    var countDescription = DescribeCountType(countType);
    var auditMessage =
        $"{actor} a terminé {zoneDescription} pour un {countDescription} le {timestamp} UTC ({response.ItemsCount} références, total {response.TotalQuantity}).";

    await auditLogger.LogAsync(auditMessage, operatorName, "inventories.complete.success").ConfigureAwait(false);

    return Results.Ok(response);
})
.WithName("CompleteInventoryRun")
.WithTags("Inventories")
.Produces<CompleteInventoryRunResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound);

// --- Restart runs d’une zone ---
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

// --- Produits ---
app.MapPost("/api/products", async (
    CreateProductRequest request,
    IDbConnection connection,
    IAuditLogger auditLogger,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (request is null)
    {
        return Results.BadRequest(new { message = "Le corps de la requête est requis." });
    }

    var sanitizedSku = request.Sku?.Trim();
    var sanitizedName = request.Name?.Trim();
    var sanitizedEan = string.IsNullOrWhiteSpace(request.Ean) ? null : request.Ean.Trim();

    if (string.IsNullOrWhiteSpace(sanitizedSku))
    {
        await LogProductCreationAttemptAsync(auditLogger, httpContext, "sans SKU", "products.create.invalid").ConfigureAwait(false);
        return Results.BadRequest(new { message = "Le SKU est requis." });
    }

    if (sanitizedSku.Length > 32)
    {
        await LogProductCreationAttemptAsync(auditLogger, httpContext, "avec un SKU trop long", "products.create.invalid").ConfigureAwait(false);
        return Results.BadRequest(new { message = "Le SKU ne peut pas dépasser 32 caractères." });
    }

    if (string.IsNullOrWhiteSpace(sanitizedName))
    {
        await LogProductCreationAttemptAsync(auditLogger, httpContext, "sans nom", "products.create.invalid").ConfigureAwait(false);
        return Results.BadRequest(new { message = "Le nom est requis." });
    }

    if (sanitizedName.Length > 256)
    {
        await LogProductCreationAttemptAsync(auditLogger, httpContext, "avec un nom trop long", "products.create.invalid").ConfigureAwait(false);
        return Results.BadRequest(new { message = "Le nom ne peut pas dépasser 256 caractères." });
    }

    if (sanitizedEan is not null)
    {
        if (sanitizedEan.Length is < 8 or > 13)
        {
            await LogProductCreationAttemptAsync(auditLogger, httpContext, "avec un EAN invalide", "products.create.invalid").ConfigureAwait(false);
            return Results.BadRequest(new { message = "L'EAN doit contenir entre 8 et 13 caractères." });
        }

        if (!sanitizedEan.All(char.IsDigit))
        {
            await LogProductCreationAttemptAsync(auditLogger, httpContext, "avec un EAN non numérique", "products.create.invalid").ConfigureAwait(false);
            return Results.BadRequest(new { message = "L'EAN doit contenir uniquement des chiffres." });
        }
    }

    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

    const string skuExistsSql = "SELECT 1 FROM \"Product\" WHERE LOWER(\"Sku\") = LOWER(@Sku) LIMIT 1";
    var skuExists = await connection.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(skuExistsSql, new { Sku = sanitizedSku }, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    if (skuExists.HasValue)
    {
        await LogProductCreationAttemptAsync(auditLogger, httpContext, $"avec un SKU déjà utilisé ({sanitizedSku})", "products.create.conflict").ConfigureAwait(false);
        return Results.Conflict(new { message = "Un produit avec ce SKU existe déjà." });
    }

    if (sanitizedEan is not null)
    {
        const string eanExistsSql = "SELECT 1 FROM \"Product\" WHERE \"Ean\" = @Ean LIMIT 1";
        var eanExists = await connection.QuerySingleOrDefaultAsync<int?>(
                new CommandDefinition(eanExistsSql, new { Ean = sanitizedEan }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (eanExists.HasValue)
        {
            await LogProductCreationAttemptAsync(auditLogger, httpContext, $"avec un EAN déjà utilisé ({sanitizedEan})", "products.create.conflict").ConfigureAwait(false);
            return Results.Conflict(new { message = "Un produit avec cet EAN existe déjà." });
        }
    }

    var now = DateTimeOffset.UtcNow;
    var newProductId = Guid.NewGuid();

    const string insertSql = @"
INSERT INTO ""Product"" (""Id"", ""Sku"", ""Name"", ""Ean"", ""CreatedAtUtc"")
VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc)
RETURNING ""Id"", ""Sku"", ""Name"", ""Ean"";";

    var createdProduct = await connection.QuerySingleAsync<ProductDto>(
            new CommandDefinition(
                insertSql,
                new
                {
                    Id = newProductId,
                    Sku = sanitizedSku,
                    Name = sanitizedName,
                    Ean = sanitizedEan,
                    CreatedAtUtc = now
                },
                cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    var userName = GetAuthenticatedUserName(httpContext);
    var actor = FormatActorLabel(userName);
    var timestamp = FormatTimestamp(now);
    var eanLabel = string.IsNullOrWhiteSpace(createdProduct.Ean) ? "non renseigné" : createdProduct.Ean;
    var creationMessage = $"{actor} a créé le produit \"{createdProduct.Name}\" (SKU {createdProduct.Sku}, EAN {eanLabel}) le {timestamp} UTC.";
    await auditLogger.LogAsync(creationMessage, userName, "products.create.success").ConfigureAwait(false);

    var location = $"/api/products/{Uri.EscapeDataString(createdProduct.Sku)}";
    return Results.Created(location, createdProduct);
})
.WithName("CreateProduct")
.WithTags("Produits")
.Produces<ProductDto>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status409Conflict)
.WithOpenApi(op =>
{
    op.Summary = "Crée un nouveau produit.";
    op.Description = "Permet l'ajout manuel d'un produit en spécifiant son SKU, son nom et éventuellement un code EAN.";
    return op;
});

app.MapGet("/api/products/{code}", async (
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
    var candidateEans = BuildCandidateEanCodes(sanitizedCode);

    ProductDto? product = null;
    if (candidateEans.Length > 0)
    {
        var dynamicParameters = new DynamicParameters();
        var parameterNames = new string[candidateEans.Length];

        for (var index = 0; index < candidateEans.Length; index++)
        {
            var parameterName = $"Code{index}";
            parameterNames[index] = parameterName;
            dynamicParameters.Add(parameterName, candidateEans[index]);
        }

        var conditions = string.Join(" OR ", parameterNames.Select(name => $"\"Ean\" = @{name}"));
        var sql = $"SELECT \"Id\", \"Sku\", \"Name\", \"Ean\" FROM \"Product\" WHERE {conditions} LIMIT 1";

        product = await connection
            .QuerySingleOrDefaultAsync<ProductDto>(
                new CommandDefinition(sql, dynamicParameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    if (product is null)
    {
        product = await connection.QuerySingleOrDefaultAsync<ProductDto>(
            new CommandDefinition(
                "SELECT \"Id\", \"Sku\", \"Name\", \"Ean\" FROM \"Product\" WHERE LOWER(\"Sku\") = LOWER(@Code) LIMIT 1",
                new { Code = sanitizedCode },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
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
})
.WithName("GetProductByCode")
.WithTags("Produits")
.Produces<ProductDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound)
.WithOpenApi(op =>
{
    op.Summary = "Recherche un produit par code scanné.";
    op.Description = "Retourne un produit à partir de son SKU ou d'un code EAN scanné.";
    return op;
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

static string[] BuildCandidateEanCodes(string code)
{
    if (string.IsNullOrWhiteSpace(code))
    {
        return Array.Empty<string>();
    }

    if (code.Length > 32)
    {
        return Array.Empty<string>();
    }

    if (!code.All(char.IsDigit))
    {
        return Array.Empty<string>();
    }

    var candidates = new List<string>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    void AddCandidate(string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        if (seen.Add(value))
        {
            candidates.Add(value);
        }
    }

    AddCandidate(code);

    var index = 0;
    while (index < code.Length && code[index] == '0')
    {
        index++;
        var candidate = code[index..];
        if (candidate.Length == 0)
        {
            candidate = "0";
        }

        AddCandidate(candidate);
    }

    var trimmed = code.TrimStart('0');
    if (trimmed.Length == 0)
    {
        trimmed = "0";
    }

    if (trimmed.Length < 8)
    {
        AddCandidate(trimmed.PadLeft(8, '0'));
    }

    if (trimmed.Length < 13)
    {
        AddCandidate(trimmed.PadLeft(13, '0'));
    }

    return candidates.ToArray();
}

static string BuildUnknownSku(string ean)
{
    if (string.IsNullOrWhiteSpace(ean))
    {
        return $"UNK-{Guid.NewGuid():N}"[..32];
    }

    var normalized = ean.Trim();
    if (normalized.Length > 13)
    {
        normalized = normalized[^13..];
    }

    var sku = $"UNK-{normalized}";
    if (sku.Length <= 32)
    {
        return sku;
    }

    return sku[^32..];
}

static async Task<bool> TableExistsAsync(IDbConnection connection, string tableName, CancellationToken cancellationToken)
{
    const string sql = @"SELECT 1
FROM information_schema.tables
WHERE table_schema = current_schema()
  AND table_name = @TableName
LIMIT 1;";

    var result = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(sql, new { TableName = tableName }, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    return result.HasValue;
}

static async Task<bool> ColumnExistsAsync(IDbConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
{
    const string sql = @"SELECT 1
FROM information_schema.columns
WHERE table_schema = current_schema()
  AND table_name = @TableName
  AND column_name = @ColumnName
LIMIT 1;";

    var result = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(sql, new { TableName = tableName, ColumnName = columnName }, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    return result.HasValue;
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

static async Task LogProductCreationAttemptAsync(
    IAuditLogger auditLogger,
    HttpContext httpContext,
    string details,
    string category)
{
    var now = DateTimeOffset.UtcNow;
    var userName = GetAuthenticatedUserName(httpContext);
    var actor = FormatActorLabel(userName);
    var timestamp = FormatTimestamp(now);
    var message = $"{actor} a tenté de créer un produit {details} le {timestamp} UTC.";
    await auditLogger.LogAsync(message, userName, category).ConfigureAwait(false);
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

internal sealed record SanitizedCountLine(string Ean, decimal Quantity, bool IsManual);
internal sealed record CountingRunRow(Guid Id, Guid InventorySessionId, Guid LocationId, short CountType);
internal sealed record ProductLookupRow(Guid Id, string Ean);

// Types internes mappés Dapper
internal sealed class LocationCountStatusRow
{
    public Guid LocationId { get; set; }
    public short CountType { get; set; }
    public Guid? RunId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? OperatorDisplayName { get; set; }
}

internal sealed class OpenRunSummaryRow
{
    public Guid RunId { get; set; }
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = "";
    public string LocationLabel { get; set; } = "";
    public short CountType { get; set; }
    public string? OperatorDisplayName { get; set; }
    public DateTime StartedAtUtc { get; set; }
}

internal sealed class ConflictSummaryRow
{
    public Guid ConflictId { get; set; }
    public Guid CountLineId { get; set; }
    public Guid CountingRunId { get; set; }
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = "";
    public string LocationLabel { get; set; } = "";
    public short CountType { get; set; }
    public string? OperatorDisplayName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public partial class Program { }

// ===== Utilitaire temps (deux surcharges) =====
internal static class TimeUtil
{
    // non-nullable -> non-nullable
    public static DateTimeOffset ToUtcOffset(DateTime dt) =>
        new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));

    // nullable -> nullable
    public static DateTimeOffset? ToUtcOffset(DateTime? dt) =>
        dt.HasValue ? ToUtcOffset(dt.Value) : (DateTimeOffset?)null;
}
