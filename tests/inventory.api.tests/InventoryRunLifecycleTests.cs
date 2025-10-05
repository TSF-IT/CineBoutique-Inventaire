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
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class InventoryRunLifecycleTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;

    public InventoryRunLifecycleTests(PostgresTestContainerFixture pg)
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
    public async Task StartInventoryRun_CreatesRunAndMarksLocationBusy()
    {
        await ResetDatabaseAsync();
        var (locationId, shopId) = await SeedLocationAsync("S1", "Zone S1");
        var ownerUserId = await SeedShopUserAsync(shopId, "Amélie");

        var request = new StartRunRequest(shopId, ownerUserId, 1);

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/start", request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<StartInventoryRunResponse>();
        Assert.NotNull(payload);
        Assert.Equal(locationId, payload!.LocationId);
        Assert.Equal((short)1, payload.CountType);
        Assert.NotEqual(Guid.Empty, payload.RunId);
        Assert.Equal(ownerUserId, payload.OwnerUserId);
        Assert.Equal("Amélie", payload.OwnerDisplayName);

        var locationsResponse = await _client.GetAsync($"/api/locations?shopId={shopId}");
        locationsResponse.EnsureSuccessStatusCode();
        var locations = await locationsResponse.Content.ReadFromJsonAsync<List<LocationResponse>>();
        Assert.NotNull(locations);
        var single = Assert.Single(locations!.Where(item => item.Code == "S1"));
        Assert.True(single.IsBusy);
        Assert.Equal(payload.RunId, single.ActiveRunId);
        Assert.Equal((short)1, single.ActiveCountType);

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var hasOperatorColumn = await CountingRunSqlHelper.HasOperatorDisplayNameAsync(connection);

        var runQuery = hasOperatorColumn
            ? "SELECT \"Id\" AS RunId, \"StartedAtUtc\" AS StartedAt, \"CompletedAtUtc\" AS CompletedAt, \"OperatorDisplayName\" AS Operator FROM \"CountingRun\" WHERE \"Id\" = @RunId"
            : "SELECT \"Id\" AS RunId, \"StartedAtUtc\" AS StartedAt, \"CompletedAtUtc\" AS CompletedAt, NULL::text AS Operator FROM \"CountingRun\" WHERE \"Id\" = @RunId";

        var run = await connection.QuerySingleAsync<(Guid RunId, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string? Operator)>(
            runQuery,
            new { payload.RunId });

        Assert.Equal(payload.RunId, run.RunId);
        Assert.Null(run.CompletedAt);

        if (hasOperatorColumn)
        {
            Assert.Equal("Amélie", single.BusyBy);
            Assert.Equal("Amélie", run.Operator);
        }
        else
        {
            Assert.Null(single.BusyBy);
            Assert.Null(run.Operator);
        }
    }

    [Fact]
    public async Task StartInventoryRun_ReturnsConflict_WhenOtherOperatorActive()
    {
        await ResetDatabaseAsync();
        var (locationId, shopId) = await SeedLocationAsync("S1", "Zone S1");

        var firstOwnerId = await SeedShopUserAsync(shopId, "Amélie");
        var firstRequest = new StartRunRequest(shopId, firstOwnerId, 1);
        var firstResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/start", firstRequest);
        firstResponse.EnsureSuccessStatusCode();

        var secondOwnerId = await SeedShopUserAsync(shopId, "Bruno");
        var secondRequest = new StartRunRequest(shopId, secondOwnerId, 1);

        var secondResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/start", secondRequest);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task StartInventoryRun_ReturnsNotFound_WhenLocationBelongsToAnotherShop()
    {
        await ResetDatabaseAsync();
        var (locationId, _) = await SeedLocationAsync("S1", "Zone S1");
        var otherShopId = await SeedShopAsync("CinéBoutique Lyon");
        var ownerUserId = await SeedShopUserAsync(otherShopId, "Lucie");

        var request = new StartRunRequest(otherShopId, ownerUserId, 1);

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/start", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Ressource introuvable", problem!.Title);
        Assert.Equal("La zone demandée est introuvable.", problem.Detail);

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var runsCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM \"CountingRun\" WHERE \"LocationId\" = @LocationId",
            new { LocationId = locationId });

        Assert.Equal(0, runsCount);
    }

    [Fact]
    public async Task StartInventoryRun_ReturnsBadRequest_ForInvalidPayload()
    {
        await ResetDatabaseAsync();
        var (locationId, shopId) = await SeedLocationAsync("S1", "Zone S1");

        var request = new StartRunRequest(shopId, Guid.Empty, 5);

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/start", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem!.Errors);
        Assert.True(problem.Errors.ContainsKey("OwnerUserId"));
        Assert.True(problem.Errors.ContainsKey("CountType"));
    }

    [Fact]
    public async Task AbortInventoryRun_ReleasesZone()
    {
        await ResetDatabaseAsync();
        var (locationId, shopId) = await SeedLocationAsync("S1", "Zone S1");
        var ownerUserId = await SeedShopUserAsync(shopId, "Amélie");

        var startRequest = new StartRunRequest(shopId, ownerUserId, 1);
        var startResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/start", startRequest);
        startResponse.EnsureSuccessStatusCode();
        var startPayload = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>();
        Assert.NotNull(startPayload);

        var abortResponse = await _client.DeleteAsync($"/api/inventories/{locationId}/runs/{startPayload!.RunId}?ownerUserId={ownerUserId}");
        Assert.Equal(HttpStatusCode.NoContent, abortResponse.StatusCode);

        var locationsResponse = await _client.GetAsync($"/api/locations?shopId={shopId}");
        locationsResponse.EnsureSuccessStatusCode();
        var locations = await locationsResponse.Content.ReadFromJsonAsync<List<LocationResponse>>();
        Assert.NotNull(locations);
        var single = Assert.Single(locations!.Where(item => item.Code == "S1"));
        Assert.False(single.IsBusy);
        Assert.Null(single.ActiveRunId);

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var countRuns = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM \"CountingRun\" WHERE \"LocationId\" = @LocationId",
            new { LocationId = locationId });
        Assert.Equal(0, countRuns);
    }

    [Fact]
    public async Task ReleaseInventoryRun_ReturnsNotFound_WhenRunMissing()
    {
        await ResetDatabaseAsync();
        var (locationId, shopId) = await SeedLocationAsync("S1", "Zone S1");
        var ownerUserId = await SeedShopUserAsync(shopId, "Amélie");

        var request = new ReleaseRunRequest(Guid.NewGuid(), ownerUserId);

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/release", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Ressource introuvable", problem!.Title);
        Assert.Equal("Aucun comptage actif pour les critères fournis.", problem.Detail);
    }

    [Fact]
    public async Task ReleaseInventoryRun_ReturnsConflict_WhenHeldByOtherOperator()
    {
        await ResetDatabaseAsync();
        var (locationId, shopId) = await SeedLocationAsync("S1", "Zone S1");
        var ownerAmelie = await SeedShopUserAsync(shopId, "Amélie");
        var ownerBruno = await SeedShopUserAsync(shopId, "Bruno");

        var startResponse = await _client.PostAsJsonAsync(
            $"/api/inventories/{locationId}/start",
            new StartRunRequest(shopId, ownerAmelie, 1));
        startResponse.EnsureSuccessStatusCode();
        var startPayload = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>();
        Assert.NotNull(startPayload);

        var releaseRequest = new ReleaseRunRequest(startPayload!.RunId, ownerBruno);
        var releaseResponse = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/release", releaseRequest);

        Assert.Equal(HttpStatusCode.Conflict, releaseResponse.StatusCode);

        var problem = await releaseResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Conflit", problem!.Title);
        Assert.NotNull(problem.Detail);
        Assert.Contains("Comptage détenu", problem.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleaseInventoryRun_ReturnsBadRequest_ForInvalidPayload()
    {
        await ResetDatabaseAsync();
        var (locationId, _) = await SeedLocationAsync("S1", "Zone S1");

        var request = new ReleaseRunRequest(Guid.Empty, Guid.Empty);

        var response = await _client.PostAsJsonAsync($"/api/inventories/{locationId}/release", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("RunId"));
        Assert.True(problem.Errors.ContainsKey("OwnerUserId"));
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

    private static async Task EnsureConnectionOpenAsync(IDbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            await ((DbConnection)connection).OpenAsync();
        }
    }

    private async Task<Guid> SeedShopUserAsync(Guid shopId, string displayName)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var userId = Guid.NewGuid();
        const string insertSql =
            "INSERT INTO \"ShopUser\" (\"Id\", \"ShopId\", \"Login\", \"DisplayName\", \"IsAdmin\", \"Secret_Hash\", \"Disabled\")" +
            " VALUES (@Id, @ShopId, @Login, @DisplayName, FALSE, '', FALSE);";

        await connection.ExecuteAsync(
            insertSql,
            new
            {
                Id = userId,
                ShopId = shopId,
                Login = $"user_{Guid.NewGuid():N}",
                DisplayName = displayName
            });

        return userId;
    }

    private async Task<(Guid LocationId, Guid ShopId)> SeedLocationAsync(string code, string label)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var shopId = await SeedShopAsync("CinéBoutique Paris");

        var locationId = Guid.NewGuid();
        const string insertLocationSql =
            "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\", \"ShopId\") VALUES (@Id, @Code, @Label, @ShopId);";
        await connection.ExecuteAsync(
            insertLocationSql,
            new
            {
                Id = locationId,
                Code = code,
                Label = label,
                ShopId = shopId
            });
        return (locationId, shopId);
    }

    private async Task<Guid> SeedShopAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string ensureShopSql =
            "INSERT INTO \"Shop\" (\"Name\") VALUES (@Name) ON CONFLICT DO NOTHING;";
        const string selectShopSql =
            "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name) LIMIT 1;";

        await connection.ExecuteAsync(ensureShopSql, new { Name = name });
        return await connection.ExecuteScalarAsync<Guid>(selectShopSql, new { Name = name });
    }
}

public sealed class LocationResponse
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public bool IsBusy { get; set; }

    public Guid? ActiveRunId { get; set; }

    public short? ActiveCountType { get; set; }

    public string? BusyBy { get; set; }
}
