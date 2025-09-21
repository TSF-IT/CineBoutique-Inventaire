using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using CineBoutique.Inventory.Infrastructure.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class InventoryApiApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public InventoryApiApplicationFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public ConnectionCounter ConnectionCounter => Services.GetRequiredService<ConnectionCounter>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
}
