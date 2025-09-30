#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class InventoryCompletionEndpointTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public InventoryCompletionEndpointTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString);
        await _factory.EnsureMigratedAsync().ConfigureAwait(false);
        _client = _factory.CreateClient();
        await ResetDatabaseAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CompleteInventoryRun_ReturnsBadRequest_WhenNoItems()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);
        var locationId = await SeedLocationAsync("S1", "Zone S1").ConfigureAwait(false);

        var payload = new CompleteInventoryRunRequest
        {
            CountType = 1,
            Operator = "Amélie",
            Items = new List<CompleteInventoryRunItemRequest>()
        };

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", payload).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompleteInventoryRun_CreatesRunAndLines_ForExistingProduct()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);
        var locationId = await SeedLocationAsync("S1", "Zone S1").ConfigureAwait(false);
        var productId = await SeedProductAsync("PROD-001", "Produit référencé", "12345678").ConfigureAwait(false);

        var payload = new CompleteInventoryRunRequest
        {
            CountType = 1,
            Operator = "Amélie",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "12345678",
                    Quantity = 2,
                    IsManual = false
                }
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", payload).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CompleteInventoryRunResponse>().ConfigureAwait(false);
        Assert.NotNull(result);
        Assert.Equal(locationId, result!.LocationId);
        Assert.Equal(1, result.ItemsCount);
        Assert.Equal(2m, result.TotalQuantity);

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection).ConfigureAwait(false);

        var runs = await connection.QueryAsync<(Guid RunId, Guid SessionId, DateTimeOffset? CompletedAt, string? Operator)>(
                "SELECT \"Id\" AS RunId, \"InventorySessionId\" AS SessionId, \"CompletedAtUtc\" AS CompletedAt, \"OperatorDisplayName\" AS Operator FROM \"CountingRun\"")
            .ConfigureAwait(false);

        Assert.Single(runs);
        var singleRun = runs.Single();
        Assert.Equal(result.RunId, singleRun.RunId);
        Assert.NotNull(singleRun.CompletedAt);
        Assert.Equal("Amélie", singleRun.Operator);

        var lines = await connection.QueryAsync<(Guid ProductId, decimal Quantity)>(
                "SELECT \"ProductId\", \"Quantity\" FROM \"CountLine\"")
            .ConfigureAwait(false);

        Assert.Single(lines);
        var line = lines.Single();
        Assert.Equal(productId, line.ProductId);
        Assert.Equal(2m, line.Quantity);
    }

    [Fact]
    public async Task CompleteInventoryRun_CreatesUnknownProduct_WhenEanNotFound()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);
        var locationId = await SeedLocationAsync("S1", "Zone S1").ConfigureAwait(false);

        var payload = new CompleteInventoryRunRequest
        {
            CountType = 2,
            Operator = "Bruno",
            Items = new List<CompleteInventoryRunItemRequest>
            {
                new()
                {
                    Ean = "99999999",
                    Quantity = 5,
                    IsManual = true
                }
            }
        };

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/complete", payload).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection).ConfigureAwait(false);

        var product = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Sku, string Name)>(
                "SELECT \"Id\", \"Sku\", \"Name\" FROM \"Product\" WHERE \"Ean\" = @Ean LIMIT 1",
                new { Ean = "99999999" })
            .ConfigureAwait(false);

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.StartsWith("UNK-", product.Sku, StringComparison.Ordinal);
        Assert.Equal("Produit inconnu EAN 99999999", product.Name);

        var lines = await connection.QueryAsync<(Guid ProductId, decimal Quantity)>(
                "SELECT \"ProductId\", \"Quantity\" FROM \"CountLine\"")
            .ConfigureAwait(false);

        Assert.Single(lines);
        var line = lines.Single();
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(5m, line.Quantity);
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection).ConfigureAwait(false);

        const string cleanupSql = @"
TRUNCATE TABLE ""Audit"" RESTART IDENTITY CASCADE;
TRUNCATE TABLE ""CountLine"" RESTART IDENTITY CASCADE;
TRUNCATE TABLE ""CountingRun"" RESTART IDENTITY CASCADE;
TRUNCATE TABLE ""InventorySession"" RESTART IDENTITY CASCADE;
TRUNCATE TABLE ""Product"" RESTART IDENTITY CASCADE;
TRUNCATE TABLE ""Location"" RESTART IDENTITY CASCADE;
TRUNCATE TABLE ""audit_logs"" RESTART IDENTITY CASCADE;";

        await connection.ExecuteAsync(cleanupSql).ConfigureAwait(false);
    }

    private async Task<Guid> SeedLocationAsync(string code, string label)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection).ConfigureAwait(false);

        var locationId = Guid.NewGuid();
        const string sql = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, @Code, @Label);";
        await connection.ExecuteAsync(sql, new { Id = locationId, Code = code, Label = label }).ConfigureAwait(false);
        return locationId;
    }

    private async Task<Guid> SeedProductAsync(string sku, string name, string ean)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection).ConfigureAwait(false);

        var productId = Guid.NewGuid();
        const string sql =
            "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc);";
        await connection.ExecuteAsync(sql, new { Id = productId, Sku = sku, Name = name, Ean = ean, CreatedAtUtc = DateTimeOffset.UtcNow }).ConfigureAwait(false);
        return productId;
    }

    private static async Task EnsureConnectionOpenAsync(IDbConnection connection)
    {
        switch (connection)
        {
            case DbConnection dbConnection when dbConnection.State != ConnectionState.Open:
                await dbConnection.OpenAsync().ConfigureAwait(false);
                break;
            case { State: ConnectionState.Closed }:
                connection.Open();
                break;
        }
    }
}
