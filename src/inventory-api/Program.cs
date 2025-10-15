// Modifications : simplification de Program.cs via des extensions et intégration du mapping conflits.
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Http;
using CineBoutique.Inventory.Api.Infrastructure.Middleware;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Hosting;
using CineBoutique.Inventory.Api.Services;
using FluentValidation;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.DependencyInjection;
using CineBoutique.Inventory.Infrastructure.Migrations;
using CineBoutique.Inventory.Infrastructure.Seeding;
using FluentValidation.AspNetCore;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Processors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Serilog; // requis pour UseSerilog()
using CineBoutique.Inventory.Api.Infrastructure.Health;
using AppLog = CineBoutique.Inventory.Api.Hosting.Log;
using Dapper;
using Microsoft.OpenApi.Models;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ReadinessState>();

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
    allowedOrigins = AppDefaults.DevOrigins;
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

// === CHAÎNE DE CONNEXION UNIQUE POUR TOUT ===
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("La chaîne de connexion 'Default' doit être configurée.");
}

// Log de contrôle au démarrage (host/db utilisateur/ssl)
try
{
    var csb = new NpgsqlConnectionStringBuilder(connectionString);
    Console.WriteLine($"[DB] Host={csb.Host} Db={csb.Database} User={csb.Username} SSL={csb.SslMode}");
}
catch { /* pas bloquant en tests */ }

// Infrastructure + seeder
builder.Services.AddInventoryInfrastructure(builder.Configuration);
builder.Services.AddTransient<InventoryDataSeeder>();

builder.Services.AddHttpContextAccessor();

// --- Swagger ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "CinéBoutique Inventory API", Version = "v1" });
    c.SupportNonNullableReferenceTypes();

    var asmName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, $"{asmName}.xml");
    if (System.IO.File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// --- JSON options ---
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        o.JsonSerializerOptions.Encoder = JavaScriptEncoder.Default;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    o.SerializerOptions.Encoder = JavaScriptEncoder.Default;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CineBoutique.Inventory.Api.Validators.CreateShopRequestValidator>();

// --- Health checks (liveness + readiness) ---
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy())
    .AddCheck<DatabaseHealthCheck>("db", tags: ["ready"]);

// --- Dapper connection ---
builder.Services.AddScoped<IDbConnection>(sp =>
{
    var factory = sp.GetRequiredService<IDbConnectionFactory>();
    return factory.CreateConnection();
});

// IAuditLogger (API) -> DbAuditLogger écrit déjà dans audit_logs
builder.Services.AddScoped<IAuditLogger, DbAuditLogger>();
builder.Services.AddSingleton<IClock, SystemClock>();

// BRIDGE : remplace l'impl du Domain par le pont vers DbAuditLogger
builder.Services.AddScoped<CineBoutique.Inventory.Domain.Auditing.IAuditLogger, DomainAuditBridgeLogger>();

builder.Services.AddScoped<IShopService, ShopService>();
builder.Services.AddScoped<IShopUserService, ShopUserService>();

// --- CORS ---
const string DefaultCorsPolicy = "PublicApi";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DefaultCorsPolicy, policyBuilder =>
    {
        policyBuilder
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// --- Logging ---
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

// === FluentMigrator: UN SEUL BLOC, SUR 'Default' ===
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

// --- App ---
var app = builder.Build();

var env = app.Environment;
var readinessState = app.Services.GetRequiredService<ReadinessState>();

app.Logger.LogInformation("[API] Using ownerUserId for runs; legacy operatorName disabled for write.");

app.Logger.LogInformation("ASPNETCORE_ENVIRONMENT = {Env}", env.EnvironmentName);
var devLike = env.IsDevelopment() || env.IsEnvironment("CI") ||
              string.Equals(env.EnvironmentName, "Docker", StringComparison.OrdinalIgnoreCase);

var applyMigrations = app.Configuration.GetValue<bool>("APPLY_MIGRATIONS");
var disableMigrations = app.Configuration.GetValue<bool>("DISABLE_MIGRATIONS");
AppLog.MigrationsFlags(app.Logger, applyMigrations, disableMigrations);

var seedOnStartup = app.Configuration.GetValue<bool>("AppSettings:SeedOnStartup");
AppLog.SeedOnStartup(app.Logger, seedOnStartup);

var shouldRunE2ESeed = string.Equals(env.EnvironmentName, "Docker", StringComparison.OrdinalIgnoreCase) || seedOnStartup;

if (devLike)
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
                Detail = null // on n’expose pas le détail hors dev-like
            };

            context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(problem).ConfigureAwait(false);
        });
    });
}

// --- FORWARDED HEADERS pour proxy (Nginx) ---
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseCors(DefaultCorsPolicy);

// Liveness: toujours 200 tant que le process répond
app.MapGet("/health", () => Results.Ok("Healthy"));

// Readiness partagé via DI : 200 uniquement quand migrations/seed terminés
app.MapGet("/ready", (ReadinessState readiness) =>
    readiness.IsReady
        ? Results.Ok("Ready")
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapGet("/ready/details", (ReadinessState readiness) =>
    readiness.IsReady
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "not-ready", error = readiness.LastError }, statusCode: StatusCodes.Status503ServiceUnavailable));

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

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
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

app.UseMiddleware<LegacyOperatorNameWriteGuardMiddleware>();
app.UseMiddleware<SoftOperatorMiddleware>();

app.Use(async (ctx, next) =>
{
    try
    {
        await next().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled exception for {Method} {Path}", ctx.Request?.Method, ctx.Request?.Path);
        throw; // laisse l’exception remonter à DeveloperExceptionPage / ExceptionHandler
    }
});

app.MapHealthEndpoints();
app.MapDiagnosticsEndpoints();
app.MapInventoryEndpoints();
app.MapProductEndpoints();

app.MapControllers();

// Voir l'environnement et quelques flags runtime
app.MapGet("/__debug/env", (IConfiguration cfg, IWebHostEnvironment e) =>
{
    return Results.Ok(new
    {
        Environment = e.EnvironmentName,
        DetailedErrors = cfg["ASPNETCORE_DETAILEDERRORS"],
        ApplyMigrations = cfg["APPLY_MIGRATIONS"],
        DisableMigrations = cfg["DISABLE_MIGRATIONS"]
    });
}).AllowAnonymous();

// Valider la chaîne de connexion effective et ping DB
app.MapGet("/__debug/db", async (IDbConnection connection) =>
{
    try
    {
        var version = await connection.ExecuteScalarAsync<string>("select version();").ConfigureAwait(false);
        return Results.Ok(new { ok = true, version });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.ToString(), title: "db_ping_failed");
    }
}).AllowAnonymous();

var logger = app.Logger;

app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            if (!env.IsEnvironment("Testing"))
            {
                using var scope = app.Services.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

                try
                {
                    runner.MigrateUp();
                    logger.LogInformation("Database migrations applied.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to apply migrations");
                    throw;
                }
            }

            if (env.IsDevelopment())
            {
                using var scope = app.Services.CreateScope();
                var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();

                const int max = 10;
                for (var i = 1; i <= max; i++)
                {
                    try
                    {
                        await seeder.SeedAsync().ConfigureAwait(false);
                        logger.LogInformation("Database seeded.");
                        break;
                    }
                    catch (Npgsql.NpgsqlException ex) when (i < max)
                    {
                        logger.LogWarning(ex, "DB not ready yet (attempt {Attempt}/{Max}), retrying in 1s…", i, max);
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                }
            }

            if (!env.IsEnvironment("Testing"))
            {
                if (applyMigrations && !disableMigrations)
                {
                    const int maxAttempts = 10;
                    var attempt = 0;

                    while (true)
                    {
                        try
                        {
                            attempt++;
                            using var migrationScope = app.Services.CreateScope();
                            var runner = migrationScope.ServiceProvider.GetRequiredService<IMigrationRunner>();
                            runner.MigrateUp();

                            var connectionDetails = new NpgsqlConnectionStringBuilder(connectionString);
                            AppLog.DbHostDb(logger, $"{connectionDetails.Host}/{connectionDetails.Database}");

                            break;
                        }
                        catch (Exception ex) when (attempt < maxAttempts && ex is NpgsqlException or TimeoutException or InvalidOperationException)
                        {
                            AppLog.MigrationRetry(logger, attempt, maxAttempts, ex);
                            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                        }
                    }
                }
                else if (applyMigrations)
                {
                    AppLog.MigrationsSkipped(logger);
                }
            }

            if (seedOnStartup)
            {
                using var scope = app.Services.CreateScope();
                var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
                await seeder.SeedAsync().ConfigureAwait(false);
            }

            if (shouldRunE2ESeed)
            {
                using var scope = app.Services.CreateScope();
                var e2eSeeder = scope.ServiceProvider.GetRequiredService<InventoryE2ESeeder>();
                await e2eSeeder.SeedAsync(CancellationToken.None).ConfigureAwait(false);
                logger.LogInformation("E2E seed completed.");
            }

            readinessState.MarkReady();
            logger.LogInformation("Startup tasks completed. API is Ready.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup tasks failed.");
            readinessState.MarkFailed(ex);
        }
    });
});

await app.RunAsync().ConfigureAwait(false);

internal static class AppDefaults
{
    public static readonly string[] DevOrigins =
    [
        "http://localhost:5173",
        "http://127.0.0.1:5173",
    ];
}

// ---- placez CE BLOC tout à la fin de Program.cs, APRES app.Run(); ----
public partial class Program
{
    public sealed class ReadinessState
    {
        private volatile bool _ready;
        private volatile string? _lastError;

        public bool IsReady => _ready;
        public string? LastError => _lastError;

        public void MarkReady() => _ready = true;

        public void MarkFailed(Exception ex)
        {
            _ready = false;
            _lastError = ex.Message;
        }
    }
}

public partial class Program { }
