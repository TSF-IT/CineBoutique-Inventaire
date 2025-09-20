using System.Data;
using System.Data.Common;
using CineBoutique.Inventory.Api.Auth;
using CineBoutique.Inventory.Api.Configuration;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.DependencyInjection;
using CineBoutique.Inventory.Infrastructure.Migrations;
using CineBoutique.Inventory.Infrastructure.Seeding;
using Dapper;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Processors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
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

    builder.Services
        .AddOptions<ProcessorOptions>()
        .Configure(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(90);
            options.ProviderSwitches = string.Empty;
        });

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
    }

    app.UseSerilogRequestLogging();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    app.MapGet("/ready", async (IDbConnection connection, CancellationToken cancellationToken) =>
    {
        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);
        _ = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: cancellationToken)).ConfigureAwait(false);
        return Results.Ok(new { status = "ready" });
    });

    app.MapGet("/locations", async (IDbConnection connection, CancellationToken cancellationToken) =>
    {
        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);
        var locations = await connection.QueryAsync<LocationDto>(new CommandDefinition("SELECT \"Id\", \"Code\", \"Name\" FROM \"Location\" ORDER BY \"Code\"", cancellationToken: cancellationToken)).ConfigureAwait(false);
        return Results.Ok(locations);
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
