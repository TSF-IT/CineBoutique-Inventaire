using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public class InventoryApiApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public InventoryApiApplicationFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("CI");

        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["DISABLE_SERILOG"] = "true",
                ["DISABLE_MIGRATIONS"] = "true"
            };

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
}
