#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234
#pragma warning disable CA1859

using System;
using System.Data;
using System.Data.Common;
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
public class InventorySummaryEndpointTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public InventorySummaryEndpointTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString);

        await _factory.EnsureMigratedAsync().ConfigureAwait(false);

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
    public async Task GetInventorySummary_ReturnsZeroWhenNoData()
    {
        await ResetDatabaseAsync();

        var response = await _client.GetAsync("/api/inventories/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.ActiveSessions);
        Assert.Equal(0, payload.OpenRuns);
        Assert.Equal(0, payload.Conflicts);
        Assert.Null(payload.LastActivityUtc);
        Assert.Empty(payload.OpenRunDetails);
        Assert.Empty(payload.ConflictDetails);
    }

    [Fact]
    public async Task GetInventorySummary_ComputesLatestActivity()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
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

        var response = await _client.GetAsync("/api/inventories/summary");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.ActiveSessions);
        Assert.Equal(1, payload.OpenRuns);
        Assert.Equal(0, payload.Conflicts);
        Assert.NotNull(payload.LastActivityUtc);
        Assert.True(payload.LastActivityUtc >= countedAt.AddMinutes(-1));
        Assert.Single(payload.OpenRunDetails);
        var openRun = payload.OpenRunDetails[0];
        Assert.Equal(runId, openRun.RunId);
        Assert.Equal(locationId, openRun.LocationId);
        Assert.Equal("Z1", openRun.LocationCode);
        Assert.Equal("Zone 1", openRun.LocationLabel);
        Assert.Equal(1, openRun.CountType);
        Assert.Null(openRun.OperatorDisplayName);
        Assert.Equal(startedAt, openRun.StartedAtUtc, TimeSpan.FromSeconds(1));
        Assert.Empty(payload.ConflictDetails);
    }

    [Fact]
    public async Task GetInventorySummary_CountsUnresolvedConflicts()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var sessionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var countLineId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-30);

        const string insertLocation = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, 'Z2', 'Zone 2');";
        await connection.ExecuteAsync(insertLocation, new { Id = locationId });

        const string insertSession = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, 'Session', @StartedAt);";
        await connection.ExecuteAsync(insertSession, new { Id = sessionId, StartedAt = startedAt });

        const string insertRun =
            "INSERT INTO \"CountingRun\" (\"Id\", \"InventorySessionId\", \"LocationId\", \"StartedAtUtc\", \"CompletedAtUtc\", \"CountType\")\n" +
            "VALUES (@Id, @SessionId, @LocationId, @StartedAt, @CompletedAt, 1);";
        await connection.ExecuteAsync(insertRun, new
        {
            Id = runId,
            SessionId = sessionId,
            LocationId = locationId,
            StartedAt = startedAt,
            CompletedAt = completedAt
        });

        const string insertProduct = "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"CreatedAtUtc\") VALUES (@Id, 'SKU-2', 'Produit', @CreatedAt);";
        await connection.ExecuteAsync(insertProduct, new { Id = productId, CreatedAt = startedAt });

        const string insertCountLine =
            "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\")\n" +
            "VALUES (@Id, @RunId, @ProductId, 2, @CountedAt);";
        await connection.ExecuteAsync(insertCountLine, new
        {
            Id = countLineId,
            RunId = runId,
            ProductId = productId,
            CountedAt = completedAt
        });

        const string insertConflict =
            "INSERT INTO \"Conflict\" (\"Id\", \"CountLineId\", \"Status\", \"Notes\", \"CreatedAtUtc\") VALUES (@Id, @CountLineId, 'pending', NULL, @CreatedAt);";
        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = countLineId, CreatedAt = completedAt });

        var response = await _client.GetAsync("/api/inventories/summary");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Conflicts);
        Assert.Equal(0, payload.OpenRuns);
        Assert.Single(payload.ConflictDetails);
        var conflict = payload.ConflictDetails[0];
        Assert.Equal(locationId, conflict.LocationId);
        Assert.Equal("Z2", conflict.LocationCode);
        Assert.Equal("Zone 2", conflict.LocationLabel);
        Assert.Equal(1, conflict.CountType);
        Assert.Equal(runId, conflict.CountingRunId);
        Assert.Equal(countLineId, conflict.CountLineId);
        Assert.Empty(payload.OpenRunDetails);
    }

    [Fact]
    public async Task GetInventorySummary_ReturnsOk_WhenOperatorDisplayNameColumnMissing()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string dropColumnSql = "ALTER TABLE \"CountingRun\" DROP COLUMN IF EXISTS \"OperatorDisplayName\";";
        await connection.ExecuteAsync(dropColumnSql);

        try
        {
            var response = await _client.GetAsync("/api/inventories/summary");
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
            Assert.NotNull(payload);
        }
        finally
        {
            await _factory.EnsureMigratedAsync();
            await ResetDatabaseAsync();
        }
    }

    [Fact]
    public async Task GetInventorySummary_ReturnsOk_WhenAuditTableMissing()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string dropAuditSql = "DROP TABLE IF EXISTS \"Audit\";";
        await connection.ExecuteAsync(dropAuditSql);

        try
        {
            var response = await _client.GetAsync("/api/inventories/summary");
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
            Assert.NotNull(payload);
        }
        finally
        {
            await _factory.EnsureMigratedAsync();
            await ResetDatabaseAsync();
        }
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string cleanupSql =
            "TRUNCATE TABLE \"CountLine\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"CountingRun\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"InventorySession\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"Location\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"Product\" RESTART IDENTITY CASCADE;\n" +
            "TRUNCATE TABLE \"audit_logs\" RESTART IDENTITY CASCADE;\n" +
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
