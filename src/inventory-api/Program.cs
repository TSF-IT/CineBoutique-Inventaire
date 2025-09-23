using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Auth;
using CineBoutique.Inventory.Api.Configuration;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Npgsql;
using Serilog;

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
        .ScanIn(typeof(CreateInventorySchema).Assembly)
        .For.Migrations())
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

builder.Services.AddHealthChecks();

builder.Services.AddScoped<IDbConnection>(sp =>
{
    var factory = sp.GetRequiredService<IDbConnectionFactory>();
    return factory.CreateConnection();
});

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

if (app.Environment.IsDevelopment())
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
app.Logger.LogInformation("APPLY_MIGRATIONS={Apply}", applyMigrations);

if (applyMigrations)
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

            if (app.Configuration.GetValue<bool>("AppSettings:SeedOnStartup"))
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

app.MapHealthChecks("/healthz");

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

app.MapGet("/ready", async (IDbConnection connection, CancellationToken cancellationToken) =>
{
    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);
    _ = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: cancellationToken)).ConfigureAwait(false);
    return Results.Ok(new { status = "ready" });
});

app.MapGet("/api/inventories/summary", async (IDbConnection connection, CancellationToken cancellationToken) =>
{
    await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

    const string summarySql = @"SELECT
    (SELECT COUNT(*)::int FROM ""InventorySession"" WHERE ""CompletedAtUtc"" IS NULL) AS ""ActiveSessions"",
    (SELECT COUNT(*)::int FROM ""CountingRun"" WHERE ""CompletedAtUtc"" IS NULL) AS ""OpenRuns"",
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
    op.Description = "Fournit un aperçu synthétique incluant les sessions actives, les runs ouverts et la dernière activité.";
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
    ar.""ActiveRunId"",
    ar.""ActiveCountType"",
    ar.""ActiveStartedAtUtc""
FROM ""Location"" l
LEFT JOIN active_runs ar ON l.""Id"" = ar.""LocationId""
ORDER BY l.""Code"" ASC;";

    var locations = await connection
        .QueryAsync<LocationListItemDto>(new CommandDefinition(sql, new { CountType = countType }, cancellationToken: cancellationToken))
        .ConfigureAwait(false);

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

app.MapPost("/api/inventories/{locationId:guid}/restart", async (Guid locationId, int countType, IDbConnection connection, CancellationToken cancellationToken) =>
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
    await connection.ExecuteAsync(new CommandDefinition(sql, new { LocationId = locationId, CountType = countType, NowUtc = now }, cancellationToken: cancellationToken)).ConfigureAwait(false);

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

app.MapGet("/products/{code}", async (string code, IDbConnection connection, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(code))
    {
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

    return product is not null ? Results.Ok(product) : Results.NotFound();
});

app.MapPost("/auth/pin", (PinAuthenticationRequest request, ITokenService tokenService, IOptions<AuthenticationOptions> options) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Pin))
    {
        return Results.BadRequest(new { message = "Le code PIN est requis." });
    }

    var user = options.Value.Users.FirstOrDefault(u => string.Equals(u.Pin, request.Pin, StringComparison.Ordinal));
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var tokenResult = tokenService.GenerateToken(user.Name);
    var response = new PinAuthenticationResponse(user.Name, tokenResult.AccessToken, tokenResult.ExpiresAtUtc);
    return Results.Ok(response);
});

await app.RunAsync().ConfigureAwait(false);

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

public partial class Program
{
}
