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

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                run1Id,
                sessionId,
                locationId,
                CountType: 1,
                StartedAtUtc: createdAt,
                CompletedAtUtc: completedAtRun1,
                OperatorDisplayName: "Alice"));

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                run2Id,
                sessionId,
                locationId,
                CountType: 2,
                StartedAtUtc: createdAt.AddMinutes(15),
                CompletedAtUtc: completedAtRun2,
                OperatorDisplayName: "Bastien"));

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

        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = run1Line1, CreatedAt = completedAtRun1 });
        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = run1Line2, CreatedAt = completedAtRun1 });
        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = run2Line1, CreatedAt = completedAtRun2 });
        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = run2Line2, CreatedAt = completedAtRun2 });

        var response = await _client.GetAsync($"/api/conflicts/{locationId}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ConflictZoneDetailDto>();
        Assert.NotNull(payload);
        Assert.Equal(locationId, payload!.LocationId);
        Assert.Equal("B1", payload.LocationCode);
        Assert.Equal("Zone B1", payload.LocationLabel);
        Assert.Equal(2, payload.Runs.Count);
        Assert.Equal(2, payload.Items.Count);

        var run1Header = Assert.Single(payload.Runs, run => run.CountType == 1);
        var run2Header = Assert.Single(payload.Runs, run => run.CountType == 2);
        Assert.Equal(run1Id, run1Header.RunId);
        Assert.Equal(run2Id, run2Header.RunId);

        Assert.All(payload.Items, item => Assert.Equal(payload.Runs.Count, item.AllCounts.Count));

        var firstItem = Assert.Single(payload.Items, item => item.ProductId == product1Id);
        Assert.Equal("111", firstItem.Ean);
        Assert.Equal(5, firstItem.QtyC1);
        Assert.Equal(8, firstItem.QtyC2);
        Assert.Equal(-3, firstItem.Delta);
        Assert.Contains(firstItem.AllCounts, count => count.RunId == run1Id && count.CountType == 1 && count.Quantity == 5);
        Assert.Contains(firstItem.AllCounts, count => count.RunId == run2Id && count.CountType == 2 && count.Quantity == 8);

        var secondItem = Assert.Single(payload.Items, item => item.ProductId == product2Id);
        Assert.Equal("222", secondItem.Ean);
        Assert.Equal(3, secondItem.QtyC1);
        Assert.Equal(1, secondItem.QtyC2);
        Assert.Equal(2, secondItem.Delta);
        Assert.Contains(secondItem.AllCounts, count => count.RunId == run1Id && count.CountType == 1 && count.Quantity == 3);
        Assert.Contains(secondItem.AllCounts, count => count.RunId == run2Id && count.CountType == 2 && count.Quantity == 1);
    }

    [Fact]
    public async Task GetConflictZoneDetail_ReturnsAllRuns_WhenThreeCountsDiffer()
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
        var run3Id = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddHours(-1);

        await connection.ExecuteAsync("INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, 'D1', 'Zone D1');", new { Id = locationId });
        await connection.ExecuteAsync("INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, 'Session', @StartedAt);", new { Id = sessionId, StartedAt = createdAt });

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                run1Id,
                sessionId,
                locationId,
                CountType: 1,
                StartedAtUtc: createdAt,
                CompletedAtUtc: createdAt.AddMinutes(10),
                OperatorDisplayName: "Alice"));

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                run2Id,
                sessionId,
                locationId,
                CountType: 2,
                StartedAtUtc: createdAt.AddMinutes(20),
                CompletedAtUtc: createdAt.AddMinutes(30),
                OperatorDisplayName: "Bastien"));

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                run3Id,
                sessionId,
                locationId,
                CountType: 3,
                StartedAtUtc: createdAt.AddMinutes(40),
                CompletedAtUtc: createdAt.AddMinutes(50),
                OperatorDisplayName: "Chloé"));

        await connection.ExecuteAsync(
            "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, 'SKU-3', 'Produit 3', '333', @CreatedAt);",
            new { Id = productId, CreatedAt = createdAt });

        const string insertCountLine =
            "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@Id, @RunId, @ProductId, @Quantity, @CountedAt);";

        var line1 = Guid.NewGuid();
        var line2 = Guid.NewGuid();
        var line3 = Guid.NewGuid();

        await connection.ExecuteAsync(insertCountLine, new { Id = line1, RunId = run1Id, ProductId = productId, Quantity = 5, CountedAt = createdAt.AddMinutes(10) });
        await connection.ExecuteAsync(insertCountLine, new { Id = line2, RunId = run2Id, ProductId = productId, Quantity = 8, CountedAt = createdAt.AddMinutes(30) });
        await connection.ExecuteAsync(insertCountLine, new { Id = line3, RunId = run3Id, ProductId = productId, Quantity = 6, CountedAt = createdAt.AddMinutes(50) });

        const string insertConflict =
            "INSERT INTO \"Conflict\" (\"Id\", \"CountLineId\", \"Status\", \"CreatedAtUtc\") VALUES (@Id, @CountLineId, 'pending', @CreatedAt);";

        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = line1, CreatedAt = createdAt.AddMinutes(10) });
        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = line2, CreatedAt = createdAt.AddMinutes(30) });
        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = line3, CreatedAt = createdAt.AddMinutes(50) });

        var response = await _client.GetAsync($"/api/conflicts/{locationId}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ConflictZoneDetailDto>();
        Assert.NotNull(payload);

        Assert.Equal(3, payload!.Runs.Count);
        Assert.Collection(
            payload.Runs,
            run => Assert.Equal(1, run.CountType),
            run => Assert.Equal(2, run.CountType),
            run => Assert.Equal(3, run.CountType));

        var singleItem = Assert.Single(payload.Items);
        Assert.Equal(3, singleItem.AllCounts.Count);
        Assert.Equal(5, singleItem.AllCounts.Single(c => c.CountType == 1).Quantity);
        Assert.Equal(8, singleItem.AllCounts.Single(c => c.CountType == 2).Quantity);
        Assert.Equal(6, singleItem.AllCounts.Single(c => c.CountType == 3).Quantity);
        Assert.Equal(5, singleItem.QtyC1);
        Assert.Equal(8, singleItem.QtyC2);
        Assert.Equal(-3, singleItem.Delta);
    }

    [Fact]
    public async Task GetConflictZoneDetail_ReturnsEmpty_WhenConflictsResolved()
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
        var run3Id = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);

        await connection.ExecuteAsync("INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, 'E1', 'Zone E1');", new { Id = locationId });
        await connection.ExecuteAsync("INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, 'Session', @StartedAt);", new { Id = sessionId, StartedAt = startedAt });

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                run1Id,
                sessionId,
                locationId,
                CountType: 1,
                StartedAtUtc: startedAt,
                CompletedAtUtc: startedAt.AddMinutes(30),
                OperatorDisplayName: "Alice"));

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                run2Id,
                sessionId,
                locationId,
                CountType: 2,
                StartedAtUtc: startedAt.AddMinutes(40),
                CompletedAtUtc: startedAt.AddMinutes(70),
                OperatorDisplayName: "Bastien"));

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                run3Id,
                sessionId,
                locationId,
                CountType: 3,
                StartedAtUtc: startedAt.AddMinutes(80),
                CompletedAtUtc: startedAt.AddMinutes(110),
                OperatorDisplayName: "Chloé"));

        await connection.ExecuteAsync(
            "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, 'SKU-4', 'Produit 4', '444', @CreatedAt);",
            new { Id = productId, CreatedAt = startedAt });

        const string insertCountLine =
            "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@Id, @RunId, @ProductId, @Quantity, @CountedAt);";

        var line1 = Guid.NewGuid();
        var line2 = Guid.NewGuid();
        var line3 = Guid.NewGuid();

        await connection.ExecuteAsync(insertCountLine, new { Id = line1, RunId = run1Id, ProductId = productId, Quantity = 5, CountedAt = startedAt.AddMinutes(30) });
        await connection.ExecuteAsync(insertCountLine, new { Id = line2, RunId = run2Id, ProductId = productId, Quantity = 8, CountedAt = startedAt.AddMinutes(70) });
        await connection.ExecuteAsync(insertCountLine, new { Id = line3, RunId = run3Id, ProductId = productId, Quantity = 5, CountedAt = startedAt.AddMinutes(110) });

        const string insertConflict =
            "INSERT INTO \"Conflict\" (\"Id\", \"CountLineId\", \"Status\", \"CreatedAtUtc\", \"ResolvedAtUtc\") VALUES (@Id, @CountLineId, 'resolved', @CreatedAt, @ResolvedAt);";

        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = line2, CreatedAt = startedAt.AddMinutes(70), ResolvedAt = startedAt.AddMinutes(115) });
        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = line3, CreatedAt = startedAt.AddMinutes(110), ResolvedAt = startedAt.AddMinutes(115) });

        var response = await _client.GetAsync($"/api/conflicts/{locationId}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ConflictZoneDetailDto>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Runs);
        Assert.Empty(payload.Items);
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
        Assert.Empty(payload!.Runs);
        Assert.Empty(payload.Items);
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
