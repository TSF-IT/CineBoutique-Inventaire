using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

[Collection("ApiTestCollection")]
public class InventoryApiApplicationFactory : WebApplicationFactory<Program>
{
    private readonly PostgresTestContainerFixture _fixture;

    public InventoryApiApplicationFactory(PostgresTestContainerFixture fixture)
    {
        _fixture = fixture;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("CI");

        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["DISABLE_SERILOG"] = "true",
                ["DISABLE_MIGRATIONS"] = "true",
                ["ConnectionStrings:Default"] = _fixture.ConnectionString
            };
            cfg.AddInMemoryCollection(dict);
        });

        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            var cs = _fixture.ConnectionString ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cs) ||
                cs.Contains("127.0.0.1:5432", StringComparison.OrdinalIgnoreCase) ||
                cs.Contains("localhost:5432", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid test connection string; Testcontainers PG must be used.");
            }
        });
    }
}
