#pragma warning disable CA1707
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class SeedDemoTests : IAsyncLifetime, IDisposable
{
    private readonly PostgresTestContainerFixture _pg;
    private readonly IReadOnlyDictionary<string, string?> _authConfiguration = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Authentication:Users:0:Name"] = "Amélie",
        ["Authentication:Users:0:Pin"] = "1111",
        ["Authentication:Issuer"] = "CineBoutique.Inventory",
        ["Authentication:Audience"] = "CineBoutique.Inventory",
        ["Authentication:Secret"] = "ChangeMe-Secret-Key-For-Inventory-Api-123",
        ["Authentication:TokenLifetimeMinutes"] = "30"
    };

    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;
    private bool _disposed;

    public SeedDemoTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        // Étape 1 : appliquer les migrations sans seed pour disposer d'un schéma propre.
        using (var migrator = new InventoryApiApplicationFactory(
                   _pg.ConnectionString,
                   new Dictionary<string, string?>(_authConfiguration)
                   {
                       ["APPLY_MIGRATIONS"] = "true",
                       ["DISABLE_MIGRATIONS"] = "false",
                       ["AppSettings:SeedOnStartup"] = "false"
                   }))
        {
            await migrator.EnsureMigratedAsync().ConfigureAwait(true);

            using var scope = migrator.Services.CreateScope();
            var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            await using NpgsqlConnection connection = connectionFactory.CreateConnection();
            await connection.OpenAsync().ConfigureAwait(true);

            const string cleanupSql = @"TRUNCATE TABLE ""Product"" RESTART IDENTITY CASCADE;";
            await connection.ExecuteAsync(cleanupSql).ConfigureAwait(true);
        }

        // Étape 2 : démarrer l'API avec le seed de démonstration activé mais sans relancer les migrations.
        var configuration = new Dictionary<string, string?>(_authConfiguration)
        {
            ["APPLY_MIGRATIONS"] = "false",
            ["DISABLE_MIGRATIONS"] = "true",
            ["AppSettings:SeedOnStartup"] = "true"
        };

        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString, configuration);
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("1", "PRODUIT-0001")]
    [InlineData("0001", "PRODUIT-0001")]
    [InlineData("00000001", "PRODUIT-0001")]
    [InlineData("0000000000001", "PRODUIT-0001")]
    [InlineData("0000000000002", "PRODUIT-0002")]
    [InlineData("0000000000003", "PRODUIT-0003")]
    public async Task DemoSeed_ExposesProductsByEan(string ean, string expectedSku)
    {
        var requestUri = new Uri($"/api/products/{ean}", UriKind.Relative);
        var response = await _client.GetAsync(requestUri).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var product = await response.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(true);
        Assert.NotNull(product);
        Assert.Equal(expectedSku, product!.Sku);
        Assert.Equal(ean, product.Ean);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _client?.Dispose();
        _factory?.Dispose();
        _disposed = true;
    }
}
#pragma warning restore CA1707
