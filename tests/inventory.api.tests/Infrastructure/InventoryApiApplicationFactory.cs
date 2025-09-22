using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.Migrations;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class InventoryApiApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("cineboutique_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();
    private string? _connectionString;

    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("La connexion Postgres de test n'est pas initialisée.");

    public ConnectionCounter ConnectionCounter => Services.GetRequiredService<ConnectionCounter>();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync().ConfigureAwait(false);
        _connectionString = _postgres.GetConnectionString();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "CI");
        Environment.SetEnvironmentVariable("DISABLE_SERILOG", "true");
        Environment.SetEnvironmentVariable("DISABLE_MIGRATIONS", "true");

        await RunMigrationsAsync(ConnectionString).ConfigureAwait(false);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("CI");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "CI",
                ["DISABLE_SERILOG"] = "true",
                ["DISABLE_MIGRATIONS"] = "true",
                ["ConnectionStrings:Default"] = ConnectionString,
                ["AppSettings:SeedOnStartup"] = "false"
            };

            configurationBuilder.AddInMemoryCollection(overrides!);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbConnectionFactory>();
            services.AddSingleton<IDbConnectionFactory>(_ =>
            {
                var options = new DatabaseOptions(ConnectionString);
                return new NpgsqlConnectionFactory(options);
            });

            services.AddSingleton<ConnectionCounter>();
            services.RemoveAll<IDbConnection>();
            services.AddScoped<IDbConnection>(sp =>
            {
                var factory = sp.GetRequiredService<IDbConnectionFactory>();
                var connection = factory.CreateConnection();
                var counter = sp.GetRequiredService<ConnectionCounter>();
                return connection is DbConnection dbConnection ? new CountingDbConnection(dbConnection, counter) : connection;
            });
        });
    }

    public async Task DisposeAsync()
    {
        Dispose();

        await _postgres.DisposeAsync().ConfigureAwait(false);
    }

    private static Task RunMigrationsAsync(string connectionString)
    {
        var services = new ServiceCollection()
            .AddLogging(lb => lb.AddFluentMigratorConsole())
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(CreateInventorySchema).Assembly).For.Migrations())
            .BuildServiceProvider();

        using var scope = services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
        return Task.CompletedTask;
    }
}
