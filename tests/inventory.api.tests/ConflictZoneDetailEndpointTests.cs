#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234
#pragma warning disable CA1859

using System;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Linq;
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
public sealed class ConflictZoneDetailEndpointTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public ConflictZoneDetailEndpointTests(PostgresTestContainerFixture pg)
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
    public async Task GetConflictZoneDetail_ReturnsNotFound_WhenLocationDoesNotExist()
    {
        var response = await _client.GetAsync($"/api/conflicts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetConflictZoneDetail_ReturnsConflictItems()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var sessionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var run1Id = Guid.NewGuid();
        var run2Id = Guid.NewGuid();
        var product1Id = Guid.NewGuid();
        var product2Id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var completedAtRun1 = createdAt.AddMinutes(5);
        var completedAtRun2 = createdAt.AddMinutes(10);

        const string insertLocation = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, 'B1', 'Zone B1');";
        await connection.ExecuteAsync(insertLocation, new { Id = locationId });

        const string insertSession = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, 'Session', @StartedAt);";
        await connection.ExecuteAsync(insertSession, new { Id = sessionId, StartedAt = createdAt });

        const string insertRun =
            "INSERT INTO \"CountingRun\" (\"Id\", \"InventorySessionId\", \"LocationId\", \"CountType\", \"StartedAtUtc\", \"CompletedAtUtc\")\n" +
            "VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAt, @CompletedAt);";

        await connection.ExecuteAsync(insertRun, new
        {
            Id = run1Id,
            SessionId = sessionId,
            LocationId = locationId,
            CountType = 1,
            StartedAt = createdAt,
            CompletedAt = completedAtRun1
        });

        await connection.ExecuteAsync(insertRun, new
        {
            Id = run2Id,
            SessionId = sessionId,
            LocationId = locationId,
            CountType = 2,
            StartedAt = createdAt.AddMinutes(15),
            CompletedAt = completedAtRun2
        });

        const string insertProduct = "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, @Sku, @Name, @Ean, @CreatedAt);";
        await connection.ExecuteAsync(insertProduct, new { Id = product1Id, Sku = "SKU-1", Name = "Produit 1", Ean = "111", CreatedAt = createdAt });
        await connection.ExecuteAsync(insertProduct, new { Id = product2Id, Sku = "SKU-2", Name = "Produit 2", Ean = "222", CreatedAt = createdAt });

        const string insertCountLine =
            "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\")\n" +
            "VALUES (@Id, @RunId, @ProductId, @Quantity, @CountedAt);";

        var run1Line1 = Guid.NewGuid();
        var run1Line2 = Guid.NewGuid();
        var run2Line1 = Guid.NewGuid();
        var run2Line2 = Guid.NewGuid();

        await connection.ExecuteAsync(insertCountLine, new
        {
            Id = run1Line1,
            RunId = run1Id,
            ProductId = product1Id,
            Quantity = 5,
            CountedAt = completedAtRun1
        });

        await connection.ExecuteAsync(insertCountLine, new
        {
            Id = run1Line2,
            RunId = run1Id,
            ProductId = product2Id,
            Quantity = 3,
            CountedAt = completedAtRun1
        });

        await connection.ExecuteAsync(insertCountLine, new
        {
            Id = run2Line1,
            RunId = run2Id,
            ProductId = product1Id,
            Quantity = 8,
            CountedAt = completedAtRun2
        });

        await connection.ExecuteAsync(insertCountLine, new
        {
            Id = run2Line2,
            RunId = run2Id,
            ProductId = product2Id,
            Quantity = 1,
            CountedAt = completedAtRun2
        });

        const string insertConflict =
            "INSERT INTO \"Conflict\" (\"Id\", \"CountLineId\", \"Status\", \"CreatedAtUtc\") VALUES (@Id, @CountLineId, 'pending', @CreatedAt);";

        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = run2Line1, CreatedAt = completedAtRun2 });
        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = run2Line2, CreatedAt = completedAtRun2 });

        var response = await _client.GetAsync($"/api/conflicts/{locationId}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ConflictZoneDetailDto>();
        Assert.NotNull(payload);
        Assert.Equal(locationId, payload!.LocationId);
        Assert.Equal("B1", payload.LocationCode);
        Assert.Equal("Zone B1", payload.LocationLabel);
        Assert.Equal(2, payload.Items.Count);

        var firstItem = Assert.Single(payload.Items, item => item.ProductId == product1Id);
        Assert.Equal("111", firstItem.Ean);
        Assert.Equal(5, firstItem.QtyC1);
        Assert.Equal(8, firstItem.QtyC2);
        Assert.Equal(-3, firstItem.Delta);

        var secondItem = Assert.Single(payload.Items, item => item.ProductId == product2Id);
        Assert.Equal("222", secondItem.Ean);
        Assert.Equal(3, secondItem.QtyC1);
        Assert.Equal(1, secondItem.QtyC2);
        Assert.Equal(2, secondItem.Delta);
    }

    [Fact]
    public async Task GetConflictZoneDetail_ReturnsEmpty_WhenRunsMissing()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var locationId = Guid.NewGuid();
        const string insertLocation = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, 'C1', 'Zone C1');";
        await connection.ExecuteAsync(insertLocation, new { Id = locationId });

        var response = await _client.GetAsync($"/api/conflicts/{locationId}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ConflictZoneDetailDto>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
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
