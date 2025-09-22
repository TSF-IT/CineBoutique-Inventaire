using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "CI",
                ["DISABLE_SERILOG"] = "true",
                ["DISABLE_MIGRATIONS"] = "true",
                ["ConnectionStrings:Default"] = _fixture.ConnectionString
            };
            config.AddInMemoryCollection(dict!);
        });

        var cs = _fixture.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs) || cs.Contains("127.0.0.1", StringComparison.Ordinal) || cs.Contains("localhost:5432", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid test connection string; Testcontainers PG must be used.");
        }

        return base.CreateHost(builder);
    }
}
