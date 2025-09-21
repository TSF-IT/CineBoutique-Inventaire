using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
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
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseDefaultServiceProvider(options =>
    {
        options.ValidateOnBuild = false;
        options.ValidateScopes = false;
    });

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new RenderedCompactJsonFormatter()));

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

    const string DevCorsPolicyName = "DevelopmentCorsPolicy";
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(DevCorsPolicyName, policyBuilder =>
        {
            policyBuilder
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();

        if (app.Configuration.GetValue<bool>("AppSettings:SeedOnStartup"))
        {
            var seeder = scope.ServiceProvider.GetService<CineBoutique.Inventory.Infrastructure.Seeding.InventoryDataSeeder>();
            if (seeder is not null)
            {
                await seeder.SeedAsync().ConfigureAwait(false);
            }
        }
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseCors(DevCorsPolicyName);
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "CinéBoutique Inventory API v1");
            c.RoutePrefix = "swagger";
        });
    }

    app.UseSerilogRequestLogging();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGet("/health", () => Results.Json(new { status = "ok" }));

    app.MapControllers();

    app.MapGet("/ready", async (IDbConnection connection, CancellationToken cancellationToken) =>
    {
        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);
        _ = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: cancellationToken)).ConfigureAwait(false);
        return Results.Ok(new { status = "ready" });
    });

    app.MapGet("/api/locations", async (int? countType, IDbConnection connection, CancellationToken cancellationToken) =>
    {
        if (countType.HasValue && countType is not (1 or 2))
        {
            return Results.BadRequest(new { message = "Le paramètre countType doit valoir 1 ou 2." });
        }

        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        const string sql = @"WITH active_runs AS (
    SELECT
        cr.""LocationId"",
        cr.""Id"" AS ""RunId"",
        cr.""CountType"",
        cr.""StartedAtUtc"",
        cr.""OperatorDisplayName"",
        ROW_NUMBER() OVER (PARTITION BY cr.""LocationId"" ORDER BY cr.""StartedAtUtc"" DESC) AS rn
    FROM ""CountingRun"" cr
    WHERE cr.""CompletedAtUtc"" IS NULL
      AND (@CountType IS NULL OR cr.""CountType"" = @CountType)
)
SELECT
    l.""Id"",
    l.""Code"",
    l.""Label"",
    NULL::text AS ""Description"",
    (ar.""RunId"" IS NOT NULL) AS ""IsBusy"",
    ar.""OperatorDisplayName"" AS ""InProgressBy"",
    ar.""CountType"",
    ar.""RunId"",
    ar.""StartedAtUtc""
FROM ""Location"" l
LEFT JOIN active_runs ar ON l.""Id"" = ar.""LocationId"" AND ar.rn = 1
ORDER BY l.""Code"";";

        var locations = await connection
            .QueryAsync<LocationDto>(new CommandDefinition(sql, new { CountType = countType }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return Results.Ok(locations);
    })
    .WithName("GetLocations")
    .WithTags("Locations")
    .Produces<IEnumerable<LocationDto>>(StatusCodes.Status200OK)
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
                Description = "Type de comptage ciblé (1 pour premier passage, 2 pour second).",
                Schema = new OpenApiSchema { Type = "integer", Minimum = 1, Maximum = 2 }
            });
        }
        return op;
    });

    app.MapPost("/api/inventories/{locationId:guid}/restart", async (Guid locationId, int countType, IDbConnection connection, CancellationToken cancellationToken) =>
    {
        if (countType is not (1 or 2))
        {
            return Results.BadRequest(new { message = "Le paramètre countType doit valoir 1 ou 2." });
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

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Échec critique du démarrage de l'API.");
    throw;
}
finally
{
    Log.CloseAndFlush();
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

public partial class Program
{
}
