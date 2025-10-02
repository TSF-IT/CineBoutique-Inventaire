// Modifications : simplification de Program.cs via des extensions et intégration du mapping conflits.
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Data;
using CineBoutique.Inventory.Api.Auth;
using CineBoutique.Inventory.Api.Configuration;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Hosting;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.DependencyInjection;
using CineBoutique.Inventory.Infrastructure.Migrations;
using CineBoutique.Inventory.Infrastructure.Seeding;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Processors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Serilog; // requis pour UseSerilog()
using CineBoutique.Inventory.Api.Infrastructure.Health;
using AppLog = CineBoutique.Inventory.Api.Hosting.Log;
using Dapper;

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

// Infrastructure + seeder
builder.Services.AddInventoryInfrastructure(builder.Configuration);
builder.Services.AddTransient<InventoryDataSeeder>();

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
    .AddCheck<DatabaseHealthCheck>("db", tags: ["ready"]);

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
builder.Services.AddSingleton<ISecretHasher, BcryptSecretHasher>();

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
            .WithOrigins(AppDefaults.DevOrigins)
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
            .WithMethods(AppDefaults.CorsMethods)
            .AllowAnyHeader()
            .SetPreflightMaxAge(TimeSpan.FromHours(1));
    });
});

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();
var env = app.Environment;

app.Logger.LogInformation("ASPNETCORE_ENVIRONMENT = {Env}", env.EnvironmentName);
var devLike = env.IsDevelopment() || env.IsEnvironment("CI") || 
              string.Equals(env.EnvironmentName, "Docker", StringComparison.OrdinalIgnoreCase);

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
AppLog.MigrationsFlags(app.Logger, applyMigrations, disableMigrations);

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
            AppLog.DbHostDb(app.Logger, $"{connectionDetails.Host}/{connectionDetails.Database}");

            break;
        }
        catch (Exception ex) when (attempt < maxAttempts && ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            AppLog.MigrationRetry(app.Logger, attempt, maxAttempts, ex);
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }
}
else if (applyMigrations)
{
    AppLog.MigrationsSkipped(app.Logger);
}

var seedOnStartup = app.Configuration.GetValue<bool>("AppSettings:SeedOnStartup");
AppLog.SeedOnStartup(app.Logger, seedOnStartup);

if (seedOnStartup)
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
    await seeder.SeedAsync().ConfigureAwait(false);
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

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapDiagnosticsEndpoints();
app.MapInventoryEndpoints();
app.MapProductEndpoints();
app.MapShopEndpoints();
app.MapShopUserEndpoints();

app.MapPost("/api/auth/login", HandleLoginAsync).WithName("ApiLogin").AllowAnonymous();

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

await app.RunAsync().ConfigureAwait(false);

// =================== Handlers & helpers ===================

static async Task<IResult> HandleLoginAsync(
    LoginRequest request,
    HttpContext httpContext,
    IDbConnection connection,
    ITokenService tokenService,
    ISecretHasher secretHasher,
    IAuditLogger auditLogger,
    IWebHostEnvironment environment,
    CancellationToken cancellationToken)
{
    if (request is null || string.IsNullOrWhiteSpace(request.Login))
    {
        var timestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);
        await auditLogger.LogAsync($"Tentative de connexion rejetée : login absent le {timestamp} UTC.", null, "auth.login.failure", cancellationToken).ConfigureAwait(false);
        return Results.BadRequest(new { message = "Le login est requis." });
    }

    await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

    var login = request.Login.Trim();
    var loginLower = login.ToLowerInvariant();

    var allowSecretlessLogin = environment.IsDevelopment()
        || environment.IsEnvironment("CI")
        || environment.IsEnvironment("Testing");

    Guid? requestedShopId = null;
    if (httpContext.Request.Headers.TryGetValue("X-Shop-Id", out var shopIdValues) && shopIdValues.Count > 0)
    {
        if (Guid.TryParse(shopIdValues[0], out var parsed))
        {
            requestedShopId = parsed;
        }
        else
        {
            return Results.BadRequest(new { message = "L'identifiant de boutique est invalide." });
        }
    }

    string requestedShopName = DefaultShopName;
    if (httpContext.Request.Headers.TryGetValue("X-Shop-Name", out var shopNameValues) && shopNameValues.Count > 0 && !string.IsNullOrWhiteSpace(shopNameValues[0]))
    {
        requestedShopName = shopNameValues[0].Trim();
    }

    const string sql = @"SELECT
    su.""Id""            AS ""Id"",
    su.""ShopId""        AS ""ShopId"",
    s.""Name""           AS ""ShopName"",
    su.""Login""         AS ""Login"",
    su.""DisplayName""    AS ""DisplayName"",
    su.""IsAdmin""        AS ""IsAdmin"",
    su.""Secret_Hash""    AS ""SecretHash"",
    su.""Disabled""       AS ""Disabled""
FROM ""ShopUser"" su
JOIN ""Shop"" s ON s.""Id"" = su.""ShopId""
WHERE lower(su.""Login"") = @Login
  AND ((@ShopId IS NOT NULL AND su.""ShopId"" = @ShopId) OR (@ShopId IS NULL AND lower(s.""Name"") = lower(@ShopName)))
LIMIT 1;";

    var row = await connection.QuerySingleOrDefaultAsync<LoginUserRow>(
            new CommandDefinition(sql, new { Login = loginLower, ShopId = requestedShopId, ShopName = requestedShopName }, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

    if (row is null)
    {
        var timestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);
        await auditLogger.LogAsync($"Tentative de connexion refusée pour le login '{login}' le {timestamp} UTC.", null, "auth.login.failure", cancellationToken).ConfigureAwait(false);
        return Results.Unauthorized();
    }

    if (row.Disabled)
    {
        var timestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);
        await auditLogger.LogAsync($"Connexion refusée pour {row.DisplayName} (compte désactivé) le {timestamp} UTC.", row.DisplayName, "auth.login.failure", cancellationToken).ConfigureAwait(false);
        return Results.Unauthorized();
    }

    if (!string.IsNullOrWhiteSpace(row.SecretHash))
    {
        if (string.IsNullOrWhiteSpace(request.Secret))
        {
            return Results.BadRequest(new { message = "Le secret est requis pour ce compte." });
        }

        if (!secretHasher.Verify(request.Secret!, row.SecretHash!))
        {
            var timestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);
            await auditLogger.LogAsync($"Connexion refusée pour {row.DisplayName} (secret invalide) le {timestamp} UTC.", row.DisplayName, "auth.login.failure", cancellationToken).ConfigureAwait(false);
            return Results.Unauthorized();
        }
    }
    else if (!allowSecretlessLogin)
    {
        var timestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);
        await auditLogger.LogAsync($"Connexion refusée pour {row.DisplayName} (secret requis) le {timestamp} UTC.", row.DisplayName, "auth.login.failure", cancellationToken).ConfigureAwait(false);
        return Results.Unauthorized();
    }

    var identity = new ShopUserIdentity(row.Id, row.ShopId, row.ShopName, row.DisplayName, row.Login, row.IsAdmin);
    var token = tokenService.GenerateToken(identity);

    var response = new LoginResponse(row.Id, row.ShopId, row.ShopName, row.DisplayName, row.IsAdmin, token.AccessToken, token.ExpiresAtUtc);

    var successTimestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);
    var actor = EndpointUtilities.FormatActorLabel(row.DisplayName);
    await auditLogger.LogAsync($"{actor} s'est connecté avec succès le {successTimestamp} UTC.", row.DisplayName, "auth.login.success", cancellationToken).ConfigureAwait(false);

    return Results.Ok(response);
}

const string DefaultShopName = "CinéBoutique Paris";

internal sealed record LoginUserRow(
    Guid Id,
    Guid ShopId,
    string ShopName,
    string Login,
    string DisplayName,
    bool IsAdmin,
    string? SecretHash,
    bool Disabled);

public partial class Program { }

internal static class AppDefaults
{
    public static readonly string[] DevOrigins =
    [
        "http://localhost:5173",
        "http://127.0.0.1:5173",
    ];

    public static readonly string[] CorsMethods =
    [
        "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"
    ];
}

