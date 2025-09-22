#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA1812
#pragma warning disable CA2007
#pragma warning disable CA2234
#pragma warning disable CA1859

using System;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("ApiTestsCollection")]
public sealed class InventorySummaryEndpointTests : IAsyncLifetime
{
    private readonly TestPostgresFixture _postgresFixture;
    private InventoryApiApplicationFactory? _factory;
    private HttpClient? _client;

    public InventorySummaryEndpointTests(TestPostgresFixture postgresFixture)
    {
        _postgresFixture = postgresFixture;
    }

    private InventoryApiApplicationFactory Factory => _factory ?? throw new InvalidOperationException("Factory not initialised");

    private HttpClient Client => _client ?? throw new InvalidOperationException("Client not initialised");

    public Task InitializeAsync()
    {
        _factory = new InventoryApiApplicationFactory(_postgresFixture.ConnectionString);
        _client = Factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            _client.Dispose();
        }

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetInventorySummary_ReturnsZeroWhenNoData()
    {
        await ResetDatabaseAsync();

        var response = await Client.GetAsync("/api/inventories/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(payload);
        Assert.Equal(0, payload.ActiveSessions);
        Assert.Equal(0, payload.OpenRuns);
        Assert.Null(payload.LastActivityUtc);
    }

    [Fact]
    public async Task GetInventorySummary_ComputesLatestActivity()
    {
        await ResetDatabaseAsync();

        using var scope = Factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var sessionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var countedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        const string insertLocation = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, 'Z1', 'Zone 1');";
        await connection.ExecuteAsync(insertLocation, new { Id = locationId });

        const string insertSession = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, 'Session', @StartedAt);";
        await connection.ExecuteAsync(insertSession, new { Id = sessionId, StartedAt = startedAt });

        const string insertRun =
            "INSERT INTO \"CountingRun\" (\"Id\", \"InventorySessionId\", \"LocationId\", \"StartedAtUtc\", \"CountType\")\n" +
            "VALUES (@Id, @SessionId, @LocationId, @StartedAt, 1);";
        await connection.ExecuteAsync(insertRun, new { Id = runId, SessionId = sessionId, LocationId = locationId, StartedAt = startedAt });

        var productId = Guid.NewGuid();
        const string insertProduct = "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"CreatedAtUtc\") VALUES (@Id, 'SKU-1', 'Produit', @CreatedAt);";
        await connection.ExecuteAsync(insertProduct, new { Id = productId, CreatedAt = startedAt });

        const string insertCountLine =
            "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\")\n" +
            "VALUES (@Id, @RunId, @ProductId, 1, @CountedAt);";
        await connection.ExecuteAsync(insertCountLine, new { Id = Guid.NewGuid(), RunId = runId, ProductId = productId, CountedAt = countedAt });

        var response = await Client.GetAsync("/api/inventories/summary");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload.ActiveSessions);
        Assert.Equal(1, payload.OpenRuns);
        Assert.NotNull(payload.LastActivityUtc);
        Assert.True(payload.LastActivityUtc >= countedAt.AddMinutes(-1));
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string cleanupSql =
            "TRUNCATE TABLE \"CountLine\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"CountingRun\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"InventorySession\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"Location\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"Product\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"Audit\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"Conflict\" RESTART IDENTITY CASCADE;";

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
