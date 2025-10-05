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
    public async Task GetInventorySummary_ReturnsZeroWhenNoData()
    {
        await ResetDatabaseAsync();

        var shopId = await SeedShopAsync($"Summary-empty-{Guid.NewGuid():N}");

        var response = await _client.GetAsync($"/api/inventories/summary?shopId={shopId:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.ActiveSessions);
        Assert.Equal(0, payload.OpenRuns);
        Assert.Equal(0, payload.CompletedRuns);
        Assert.Equal(0, payload.Conflicts);
        Assert.Null(payload.LastActivityUtc);
        Assert.Empty(payload.OpenRunDetails);
        Assert.Empty(payload.CompletedRunDetails);
        Assert.Empty(payload.ConflictZones);
        Assert.Equal(payload.OpenRuns, payload.OpenRunDetails.Count);
        Assert.Equal(payload.CompletedRuns, payload.CompletedRunDetails.Count);
    }

    [Fact]
    public async Task GetInventorySummary_ComputesLatestActivity()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var shopId = await SeedShopAsync($"Summary-activity-{Guid.NewGuid():N}");

        var sessionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var countedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await InsertLocationAsync(connection, shopId, locationId, "Z1", "Zone 1");

        const string insertSession = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, 'Session', @StartedAt);";
        await connection.ExecuteAsync(insertSession, new { Id = sessionId, StartedAt = startedAt });

        var hasOwnerColumn = await CountingRunSqlHelper.HasOwnerUserIdAsync(connection);
        Assert.True(hasOwnerColumn, "La colonne OwnerUserId est requise pour ce scénario.");

        var ownerUserId = await InsertShopUserAsync(connection, shopId, "Camille");

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                runId,
                sessionId,
                locationId,
                CountType: 1,
                StartedAtUtc: startedAt,
                CompletedAtUtc: null,
                OperatorDisplayName: null,
                OwnerUserId: ownerUserId));

        var productId = Guid.NewGuid();
        const string insertProduct = "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"CreatedAtUtc\") VALUES (@Id, 'SKU-1', 'Produit', @CreatedAt);";
        await connection.ExecuteAsync(insertProduct, new { Id = productId, CreatedAt = startedAt });

        const string insertCountLine =
            "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\")\n" +
            "VALUES (@Id, @RunId, @ProductId, 1, @CountedAt);";
        await connection.ExecuteAsync(insertCountLine, new { Id = Guid.NewGuid(), RunId = runId, ProductId = productId, CountedAt = countedAt });

        var response = await _client.GetAsync($"/api/inventories/summary?shopId={shopId:D}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.ActiveSessions);
        Assert.Equal(1, payload.OpenRuns);
        Assert.Equal(0, payload.CompletedRuns);
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
        Assert.Equal("Camille", openRun.OwnerDisplayName);
        Assert.Equal(ownerUserId, openRun.OwnerUserId);
        Assert.Equal(startedAt, openRun.StartedAtUtc, TimeSpan.FromSeconds(1));
        Assert.Empty(payload.ConflictZones);
        Assert.Equal(payload.OpenRuns, payload.OpenRunDetails.Count);
        Assert.Equal(payload.CompletedRuns, payload.CompletedRunDetails.Count);
    }

    [Fact]
    public async Task GetInventorySummary_ListsCompletedRunsWithOperator()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var shopId = await SeedShopAsync($"Summary-completed-{Guid.NewGuid():N}");
        var locationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var completedAt = startedAt.AddMinutes(45);

        await InsertLocationAsync(connection, shopId, locationId, "ZC1", "Zone C1");

        const string insertSession = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, 'Session', @StartedAt);";
        await connection.ExecuteAsync(insertSession, new { Id = sessionId, StartedAt = startedAt });

        var ownerUserId = await InsertShopUserAsync(connection, shopId, "Chloé");

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                runId,
                sessionId,
                locationId,
                CountType: 1,
                StartedAtUtc: startedAt,
                CompletedAtUtc: completedAt,
                OperatorDisplayName: null,
                OwnerUserId: ownerUserId));

        var response = await _client.GetAsync($"/api/inventories/summary?shopId={shopId:D}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(payload);
        Assert.Single(payload!.CompletedRunDetails);
        Assert.Equal(payload.CompletedRuns, payload.CompletedRunDetails.Count);
        Assert.Equal(payload.OpenRuns, payload.OpenRunDetails.Count);

        var completedRun = payload.CompletedRunDetails[0];
        Assert.Equal(runId, completedRun.RunId);
        Assert.Equal(locationId, completedRun.LocationId);
        Assert.Equal("ZC1", completedRun.LocationCode);
        Assert.Equal("Zone C1", completedRun.LocationLabel);
        Assert.Equal(1, completedRun.CountType);
        Assert.Equal("Chloé", completedRun.OwnerDisplayName);
        Assert.Equal(ownerUserId, completedRun.OwnerUserId);
        Assert.Equal(completedAt, completedRun.CompletedAtUtc, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task GetInventorySummary_CountsUnresolvedConflicts()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var shopId = await SeedShopAsync($"Summary-conflicts-{Guid.NewGuid():N}");
        var sessionId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var countLineId = Guid.NewGuid();
        var secondProductId = Guid.NewGuid();
        var secondCountLineId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-30);

        await InsertLocationAsync(connection, shopId, locationId, "Z2", "Zone 2");

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
                OperatorDisplayName: "Camille"));

        const string insertProduct = "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"CreatedAtUtc\") VALUES (@Id, 'SKU-2', 'Produit', @CreatedAt);";
        await connection.ExecuteAsync(insertProduct, new { Id = productId, CreatedAt = startedAt });

        const string insertSecondProduct = "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"CreatedAtUtc\") VALUES (@Id, 'SKU-3', 'Produit 2', @CreatedAt);";
        await connection.ExecuteAsync(insertSecondProduct, new { Id = secondProductId, CreatedAt = startedAt });

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

        await connection.ExecuteAsync(insertCountLine, new
        {
            Id = secondCountLineId,
            RunId = runId,
            ProductId = secondProductId,
            CountedAt = completedAt
        });

        const string insertConflict =
            "INSERT INTO \"Conflict\" (\"Id\", \"CountLineId\", \"Status\", \"Notes\", \"CreatedAtUtc\") VALUES (@Id, @CountLineId, 'pending', NULL, @CreatedAt);";
        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = countLineId, CreatedAt = completedAt });
        await connection.ExecuteAsync(insertConflict, new { Id = Guid.NewGuid(), CountLineId = secondCountLineId, CreatedAt = completedAt });

        var response = await _client.GetAsync($"/api/inventories/summary?shopId={shopId:D}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Conflicts);
        Assert.Equal(0, payload.OpenRuns);
        Assert.Equal(1, payload.CompletedRuns);
        Assert.Single(payload.ConflictZones);
        var conflictZone = payload.ConflictZones[0];
        Assert.Equal(locationId, conflictZone.LocationId);
        Assert.Equal("Z2", conflictZone.LocationCode);
        Assert.Equal("Zone 2", conflictZone.LocationLabel);
        Assert.Equal(2, conflictZone.ConflictLines);
        Assert.Empty(payload.OpenRunDetails);
        Assert.Equal(payload.OpenRuns, payload.OpenRunDetails.Count);
        Assert.Equal(payload.CompletedRuns, payload.CompletedRunDetails.Count);
    }

    [Fact]
    public async Task GetInventorySummary_FiltersRunsByShop()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var parisShopId = await SeedShopAsync($"Summary-paris-{Guid.NewGuid():N}");
        var lyonShopId = await SeedShopAsync($"Summary-lyon-{Guid.NewGuid():N}");

        var parisLocationId = Guid.NewGuid();
        var lyonLocationId = Guid.NewGuid();
        var parisSessionId = Guid.NewGuid();
        var lyonSessionId = Guid.NewGuid();
        var parisOpenRunId = Guid.NewGuid();
        var parisCompletedRunId = Guid.NewGuid();
        var lyonOpenRunId = Guid.NewGuid();
        var parisStartedAt = DateTimeOffset.UtcNow.AddMinutes(-45);
        var parisCompletedAt = parisStartedAt.AddMinutes(20);

        await InsertLocationAsync(connection, parisShopId, parisLocationId, "P1", "Paris Zone 1");
        await InsertLocationAsync(connection, lyonShopId, lyonLocationId, "L1", "Lyon Zone 1");

        const string insertSessionSql = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, 'Session', @StartedAt);";
        await connection.ExecuteAsync(insertSessionSql, new { Id = parisSessionId, StartedAt = parisStartedAt });
        await connection.ExecuteAsync(insertSessionSql, new { Id = lyonSessionId, StartedAt = parisStartedAt });

        var parisUserId = await InsertShopUserAsync(connection, parisShopId, "Utilisateur Paris");
        var lyonUserId = await InsertShopUserAsync(connection, lyonShopId, "Utilisateur Lyon");

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                parisOpenRunId,
                parisSessionId,
                parisLocationId,
                CountType: 1,
                StartedAtUtc: parisStartedAt,
                CompletedAtUtc: null,
                OperatorDisplayName: null,
                OwnerUserId: parisUserId));

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                parisCompletedRunId,
                parisSessionId,
                parisLocationId,
                CountType: 2,
                StartedAtUtc: parisStartedAt.AddMinutes(-30),
                CompletedAtUtc: parisCompletedAt,
                OperatorDisplayName: null,
                OwnerUserId: parisUserId));

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                lyonOpenRunId,
                lyonSessionId,
                lyonLocationId,
                CountType: 1,
                StartedAtUtc: parisStartedAt,
                CompletedAtUtc: null,
                OperatorDisplayName: null,
                OwnerUserId: lyonUserId));

        var response = await _client.GetAsync($"/api/inventories/summary?shopId={parisShopId:D}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
        Assert.NotNull(payload);

        Assert.Equal(1, payload!.OpenRuns);
        var openRun = Assert.Single(payload.OpenRunDetails);
        Assert.Equal(parisOpenRunId, openRun.RunId);
        Assert.Equal(parisLocationId, openRun.LocationId);
        Assert.Equal(parisUserId, openRun.OwnerUserId);
        Assert.Equal("Utilisateur Paris", openRun.OwnerDisplayName);

        Assert.Equal(1, payload.CompletedRuns);
        var completedRun = Assert.Single(payload.CompletedRunDetails);
        Assert.Equal(parisCompletedRunId, completedRun.RunId);
        Assert.Equal(parisLocationId, completedRun.LocationId);
        Assert.Equal(parisUserId, completedRun.OwnerUserId);

        Assert.DoesNotContain(payload.OpenRunDetails, run => run.LocationId == lyonLocationId || run.OwnerUserId == lyonUserId);
        Assert.DoesNotContain(payload.CompletedRunDetails, run => run.LocationId == lyonLocationId || run.OwnerUserId == lyonUserId);

        Assert.Equal(payload.OpenRuns, payload.OpenRunDetails.Count);
        Assert.Equal(payload.CompletedRuns, payload.CompletedRunDetails.Count);
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
            var shopId = await SeedShopAsync($"Summary-operator-missing-{Guid.NewGuid():N}");
            var response = await _client.GetAsync($"/api/inventories/summary?shopId={shopId:D}");
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>();
            Assert.NotNull(payload);
        }
        finally
        {
            const string restoreColumnSql = """
ALTER TABLE "CountingRun"
    ADD COLUMN IF NOT EXISTS "OperatorDisplayName" VARCHAR(200) NOT NULL DEFAULT 'Unknown';

ALTER TABLE "CountingRun"
    ALTER COLUMN "OperatorDisplayName" DROP DEFAULT;
""";

            await connection.ExecuteAsync(restoreColumnSql);
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
            var shopId = await SeedShopAsync($"Summary-audit-missing-{Guid.NewGuid():N}");
            var response = await _client.GetAsync($"/api/inventories/summary?shopId={shopId:D}");
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

        const string cleanupSql = """
DO $do$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'Audit') THEN
        EXECUTE 'TRUNCATE TABLE "Audit" RESTART IDENTITY CASCADE;';
    END IF;
END $do$;

TRUNCATE TABLE "CountLine" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "CountingRun" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "InventorySession" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Location" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "ShopUser" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Shop" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Product" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Conflict" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql);
    }

    private async Task<Guid> SeedShopAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var shopId = Guid.NewGuid();
        const string insertShopSql = "INSERT INTO \"Shop\" (\"Id\", \"Name\") VALUES (@Id, @Name);";
        await connection.ExecuteAsync(insertShopSql, new { Id = shopId, Name = name });
        return shopId;
    }

    private static async Task<Guid> InsertShopUserAsync(IDbConnection connection, Guid shopId, string displayName)
    {
        var userId = Guid.NewGuid();
        const string insertShopUserSql = @"
INSERT INTO ""ShopUser"" (""Id"", ""ShopId"", ""Login"", ""DisplayName"", ""IsAdmin"", ""Secret_Hash"", ""Disabled"")
VALUES (@Id, @ShopId, @Login, @DisplayName, FALSE, '', FALSE);";
        await connection.ExecuteAsync(
            insertShopUserSql,
            new
            {
                Id = userId,
                ShopId = shopId,
                Login = $"user-{Guid.NewGuid():N}",
                DisplayName = displayName
            });
        return userId;
    }

    private static async Task<Guid> InsertLocationAsync(IDbConnection connection, Guid shopId, Guid locationId, string code, string label)
    {
        const string insertLocationSql = @"
INSERT INTO ""Location"" (""Id"", ""Code"", ""Label"", ""ShopId"")
VALUES (@Id, @Code, @Label, @ShopId);";
        await connection.ExecuteAsync(
            insertLocationSql,
            new { Id = locationId, Code = code, Label = label, ShopId = shopId });
        return locationId;
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
