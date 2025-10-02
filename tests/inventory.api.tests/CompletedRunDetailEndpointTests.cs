#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234
#pragma warning disable CA1859

using System;
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
using CineBoutique.Inventory.Infrastructure.Seeding;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class CompletedRunDetailEndpointTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public CompletedRunDetailEndpointTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString);
        await _factory.EnsureMigratedAsync();
        _client = _factory.CreateClient();
        await ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetCompletedRunDetail_ReturnsNotFound_WhenRunDoesNotExist()
    {
        var response = await _client.GetAsync($"/api/inventories/runs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCompletedRunDetail_ReturnsNotFound_WhenRunNotCompleted()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var sessionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        const string insertLocation = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, 'A1', 'Zone A1');";
        await connection.ExecuteAsync(insertLocation, new { Id = locationId });

        const string insertSession = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, 'Session', @StartedAt);";
        await connection.ExecuteAsync(insertSession, new { Id = sessionId, StartedAt = startedAt });

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                runId,
                sessionId,
                locationId,
                CountType: 1,
                StartedAtUtc: startedAt,
                CompletedAtUtc: null,
                OperatorDisplayName: "Alice"));

        var response = await _client.GetAsync($"/api/inventories/runs/{runId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCompletedRunDetail_ReturnsLines_WhenRunExists()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var sessionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var product1Id = Guid.NewGuid();
        var product2Id = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var completedAt = startedAt.AddMinutes(12);

        const string insertLocation = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, 'B1', 'Zone B1');";
        await connection.ExecuteAsync(insertLocation, new { Id = locationId });

        const string insertSession = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, 'Session', @StartedAt);";
        await connection.ExecuteAsync(insertSession, new { Id = sessionId, StartedAt = startedAt });

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                runId,
                sessionId,
                locationId,
                CountType: 1,
                StartedAtUtc: startedAt,
                CompletedAtUtc: completedAt,
                OperatorDisplayName: "Bastien"));

        const string insertProduct = "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, @Sku, @Name, @Ean, @CreatedAt);";
        await connection.ExecuteAsync(insertProduct, new { Id = product1Id, Sku = "SKU-1", Name = "Produit 1", Ean = "321", CreatedAt = startedAt });
        await connection.ExecuteAsync(insertProduct, new { Id = product2Id, Sku = "SKU-2", Name = "Produit 2", Ean = "654", CreatedAt = startedAt });

        const string insertLine =
            "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@Id, @RunId, @ProductId, @Quantity, @CountedAt);";

        await connection.ExecuteAsync(insertLine, new { Id = Guid.NewGuid(), RunId = runId, ProductId = product1Id, Quantity = 5.5m, CountedAt = completedAt });
        await connection.ExecuteAsync(insertLine, new { Id = Guid.NewGuid(), RunId = runId, ProductId = product2Id, Quantity = 3m, CountedAt = completedAt });

        var response = await _client.GetAsync($"/api/inventories/runs/{runId}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CompletedRunDetailDto>();
        Assert.NotNull(payload);

        Assert.Equal(runId, payload!.RunId);
        Assert.Equal(locationId, payload.LocationId);
        Assert.Equal("B1", payload.LocationCode);
        Assert.Equal("Zone B1", payload.LocationLabel);
        Assert.Equal((short)1, payload.CountType);
        Assert.Equal("Bastien", payload.OperatorDisplayName);
        Assert.Equal(2, payload.Items.Count);

        var first = Assert.Single(payload.Items, item => item.ProductId == product1Id);
        Assert.Equal("SKU-1", first.Sku);
        Assert.Equal("Produit 1", first.Name);
        Assert.Equal("321", first.Ean);
        Assert.Equal(5.5m, first.Quantity);

        var second = Assert.Single(payload.Items, item => item.ProductId == product2Id);
        Assert.Equal("SKU-2", second.Sku);
        Assert.Equal("Produit 2", second.Name);
        Assert.Equal("654", second.Ean);
        Assert.Equal(3m, second.Quantity);
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string cleanupSql =
            "TRUNCATE TABLE \"Conflict\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"CountLine\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"CountingRun\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"InventorySession\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"Location\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"Product\" RESTART IDENTITY CASCADE;";

        await connection.ExecuteAsync(cleanupSql);

        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();
    }

    private static async Task EnsureConnectionOpenAsync(IDbConnection connection)
    {
        switch (connection)
        {
            case DbConnection dbConnection when dbConnection.State != ConnectionState.Open:
                await dbConnection.OpenAsync();
                break;
            case { State: ConnectionState.Closed }:
                connection.Open();
                break;
        }
    }
}
