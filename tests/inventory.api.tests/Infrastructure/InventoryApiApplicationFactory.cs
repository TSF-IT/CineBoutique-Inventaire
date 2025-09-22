using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using CineBoutique.Inventory.Infrastructure.Database;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class InventoryApiApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public InventoryApiApplicationFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "CI");
        Environment.SetEnvironmentVariable("DISABLE_SERILOG", "true");
        Environment.SetEnvironmentVariable("DISABLE_MIGRATIONS", "true");
    }

    public ConnectionCounter ConnectionCounter => Services.GetRequiredService<ConnectionCounter>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("CI");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["AppSettings:SeedOnStartup"] = "false"
            };

            configurationBuilder.AddInMemoryCollection(overrides!);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbConnectionFactory>();
            services.AddSingleton<IDbConnectionFactory>(provider =>
            {
                var options = new DatabaseOptions(_connectionString);
                return new NpgsqlConnectionFactory(options);
            });

            services.AddSingleton<ConnectionCounter>();
            services.RemoveAll<IDbConnection>();
            services.AddScoped<IDbConnection>(sp =>
            {
                var innerFactory = sp.GetRequiredService<IDbConnectionFactory>();
                var connection = innerFactory.CreateConnection();
                var counter = sp.GetRequiredService<ConnectionCounter>();
                return connection is DbConnection dbConnection ? new CountingDbConnection(dbConnection, counter) : connection;
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();

        return host;
    }
}
