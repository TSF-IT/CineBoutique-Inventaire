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
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Serilog; // requis pour UseSerilog()
using CineBoutique.Inventory.Api.Infrastructure.Health;
using AppLog = CineBoutique.Inventory.Api.Hosting.Log;

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

            var seedOnStartup = app.Configuration.GetValue<bool>("AppSettings:SeedOnStartup");
            AppLog.SeedOnStartup(app.Logger, seedOnStartup);

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
            AppLog.MigrationRetry(app.Logger, attempt, maxAttempts, ex);
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }
}
else if (applyMigrations)
{
    AppLog.MigrationsSkipped(app.Logger);
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

app.MapHealthEndpoints();
app.MapDiagnosticsEndpoints();
app.MapInventoryEndpoints();
app.MapProductEndpoints();

// ===== ALIAS D'AUTH PIN =====
app.MapPost("/auth/pin", HandlePinLoginAsync).WithName("PinLogin").AllowAnonymous();
app.MapPost("/api/auth/pin", HandlePinLoginAsync).WithName("ApiPinLogin").AllowAnonymous();

app.MapControllers();

await app.RunAsync().ConfigureAwait(false);

// =================== Handlers & helpers ===================

// Handler partagé pour /auth/pin et /api/auth/pin
static async Task<IResult> HandlePinLoginAsync(
    PinAuthenticationRequest request,
    ITokenService tokenService,
    IOptions<AuthenticationOptions> options,
    IAuditLogger auditLogger,
    CancellationToken cancellationToken)
{
    if (request is null || string.IsNullOrWhiteSpace(request.Pin))
    {
        var timestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);
        await auditLogger.LogAsync($"Tentative de connexion admin rejetée : code PIN absent le {timestamp} UTC.", null, "auth.pin.failure", cancellationToken).ConfigureAwait(false);
        return Results.BadRequest(new { message = "Le code PIN est requis." });
    }

    var providedPin = request.Pin.Trim();
    var user = options.Value.Users.FirstOrDefault(u => string.Equals(u.Pin, providedPin, StringComparison.Ordinal));
    if (user is null)
    {
        var timestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);
        await auditLogger.LogAsync($"Tentative de connexion admin refusée (PIN inconnu) le {timestamp} UTC.", null, "auth.pin.failure", cancellationToken).ConfigureAwait(false);
        return Results.Unauthorized();
    }

    var tokenResult = tokenService.GenerateToken(user.Name);
    var response = new PinAuthenticationResponse(user.Name, tokenResult.AccessToken, tokenResult.ExpiresAtUtc);

    var successTimestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);
    var actor = EndpointUtilities.FormatActorLabel(user.Name);
    await auditLogger.LogAsync($"{actor} s'est connecté avec succès le {successTimestamp} UTC.", user.Name, "auth.pin.success", cancellationToken).ConfigureAwait(false);

    return Results.Ok(response);
}

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

