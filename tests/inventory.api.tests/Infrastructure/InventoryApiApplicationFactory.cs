using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public class InventoryApiApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly IReadOnlyDictionary<string, string?>? _additionalConfiguration;

    public InventoryApiApplicationFactory(string connectionString, IReadOnlyDictionary<string, string?>? additionalConfiguration = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _additionalConfiguration = additionalConfiguration;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _connectionString);

        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["DISABLE_SERILOG"] = "true",
                ["DISABLE_MIGRATIONS"] = "true",
                ["ASPNETCORE_ENVIRONMENT"] = TestEnvironments.Ci
            };

            if (_additionalConfiguration is not null)
            {
                foreach (var kvp in _additionalConfiguration)
                {
                    overrides[kvp.Key] = kvp.Value;
                }
            }

            cfg.AddInMemoryCollection(overrides!);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(TestEnvironments.Ci);

        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["DISABLE_SERILOG"] = "true",
                ["DISABLE_MIGRATIONS"] = "true"
            };

            if (_additionalConfiguration is not null)
            {
                foreach (var kvp in _additionalConfiguration)
                {
                    overrides[kvp.Key] = kvp.Value;
                }
            }

            cfg.AddInMemoryCollection(overrides!);
        });

        builder.ConfigureServices(services =>
        {
            var toRemove = services
                .Where(d => d.ServiceType.FullName?.Contains("HostedService", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            foreach (var d in toRemove)
            {
                services.Remove(d);
            }
        });
    }

    public Task EnsureMigratedAsync()
    {
        using var scope = Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
        return Task.CompletedTask;
    }

    public new InventoryApiApplicationFactory WithWebHostBuilder(Action<IWebHostBuilder> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return (InventoryApiApplicationFactory)base.WithWebHostBuilder(configuration);
    }
}
