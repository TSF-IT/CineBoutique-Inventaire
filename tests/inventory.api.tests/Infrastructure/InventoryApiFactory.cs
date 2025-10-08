using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class InventoryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public InventoryApiFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["DISABLE_SERILOG"] = "true",
                ["APPLY_MIGRATIONS"] = "false",
                ["DISABLE_MIGRATIONS"] = "false",
                ["AppSettings:SeedOnStartup"] = "false"
            };

            configurationBuilder.AddInMemoryCollection(overrides!);
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.BaseAddress ??= new Uri("http://localhost");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
