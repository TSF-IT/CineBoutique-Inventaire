#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234
#pragma warning disable CA1859
#pragma warning disable CA1812

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public class LocationsEndpointTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;
    private HttpClient _client = default!;
    private static readonly Regex ActiveRunIdRegex = new(
        "^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LocationsEndpointTests(PostgresTestContainerFixture pg)
    {
        this._pg = pg;
    }

    public async Task InitializeAsync()
    {
        this._factory = new InventoryApiApplicationFactory(this._pg.ConnectionString);

        await this._factory.EnsureMigratedAsync();

        this._client = this._factory.CreateClient();

        await this.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        this._client.Dispose();
        this._factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetLocations_WithoutShopId_ReturnsBadRequestProblem()
    {
        var response = await this._client.GetAsync("/api/locations");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("ShopId requis", problem!.Detail);
    }

    [Fact]
    public async Task GetLocations_WithInvalidShopId_ReturnsBadRequestProblem()
    {
        var response = await this._client.GetAsync("/api/locations?shopId=not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("ShopId invalide", problem!.Detail);
    }

    [Fact]
    public async Task GetLocations_FiltersLocationsByShop()
    {
        await this.ResetDatabaseAsync();

        using var scope = this._factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var parisId = await EnsureShopAsync(connection, "CinéBoutique Paris");
        var brusselsId = await EnsureShopAsync(connection, "CinéBoutique Bruxelles");

        var parisLocation1 = Guid.NewGuid();
        var parisLocation2 = Guid.NewGuid();
        await InsertLocationAsync(connection, parisId, parisLocation1, "A1", "Paris A1");
        await InsertLocationAsync(connection, parisId, parisLocation2, "B1", "Paris B1");

        var brusselsLocation1 = Guid.NewGuid();
        var brusselsLocation2 = Guid.NewGuid();
        await InsertLocationAsync(connection, brusselsId, brusselsLocation1, "A1", "Bruxelles A1");
        await InsertLocationAsync(connection, brusselsId, brusselsLocation2, "C1", "Bruxelles C1");

        var parisResponse = await this._client.GetAsync($"/api/locations?shopId={parisId:D}");
        parisResponse.EnsureSuccessStatusCode();
        var parisPayload = await parisResponse.Content.ReadFromJsonAsync<List<LocationResponse>>();
        Assert.NotNull(parisPayload);
        Assert.Equal(2, parisPayload!.Count);
        var parisIds = parisPayload.Select(item => item.Id).ToList();
        Assert.Contains(parisLocation1, parisIds);
        Assert.Contains(parisLocation2, parisIds);
        Assert.DoesNotContain(brusselsLocation1, parisIds);
        Assert.DoesNotContain(brusselsLocation2, parisIds);

        var brusselsResponse = await this._client.GetAsync($"/api/locations?shopId={brusselsId:D}");
        brusselsResponse.EnsureSuccessStatusCode();
        var brusselsPayload = await brusselsResponse.Content.ReadFromJsonAsync<List<LocationResponse>>();
        Assert.NotNull(brusselsPayload);
        Assert.Equal(2, brusselsPayload!.Count);
        var brusselsIds = brusselsPayload.Select(item => item.Id).ToList();
        Assert.Contains(brusselsLocation1, brusselsIds);
        Assert.Contains(brusselsLocation2, brusselsIds);
        Assert.DoesNotContain(parisLocation1, brusselsIds);
        Assert.DoesNotContain(parisLocation2, brusselsIds);
    }

    [Fact]
    public async Task GetLocations_ReturnsBusyStatus_ForRequestedCountType()
    {
        await this.ResetDatabaseAsync();
        var seed = await this.SeedDataAsync(countType: 1);

        var response = await this._client.GetAsync($"/api/locations?shopId={seed.ShopId:D}&countType=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<List<LocationResponse>>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Count);

        var busy = payload.Single(item => item.Code == "S1");
        Assert.True(busy.IsBusy);
        Assert.Equal(seed.RunId, busy.ActiveRunId);
        var hasOperatorColumn = await this.HasOperatorDisplayNameColumnAsync();

        if (hasOperatorColumn)
        {
            Assert.Equal("alice.durand", busy.BusyBy);
        }
        else
        {
            Assert.Null(busy.BusyBy);
        }
        Assert.Equal((short)1, busy.ActiveCountType);
        Assert.NotNull(busy.ActiveStartedAtUtc);

        var free = payload.Single(item => item.Code == "S2");
        Assert.False(free.IsBusy);
        Assert.Null(free.ActiveRunId);
    }

    [Fact]
    public async Task GetLocations_WithMismatchedCountType_ReturnsFreeState()
    {
        await this.ResetDatabaseAsync();
        var seed = await this.SeedDataAsync(countType: 2);

        var response = await this._client.GetAsync($"/api/locations?shopId={seed.ShopId:D}&countType=1");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<LocationResponse>>();
        Assert.NotNull(payload);
        var busy = payload!.Single(item => item.Code == "S1");
        Assert.False(busy.IsBusy);
        Assert.Null(busy.ActiveRunId);
    }

    [Fact]
    public async Task GetLocations_WithInvalidCountType_ReturnsBadRequest()
    {
        await this.ResetDatabaseAsync();

        var seed = await this.SeedDataAsync(countType: 1);

        var response = await this._client.GetAsync($"/api/locations?shopId={seed.ShopId:D}&countType=5");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLocations_ReturnsNullForInvalidRunIdentifiers()
    {
        await this.ResetDatabaseAsync();

        var seed = await this.SeedDataAsync(countType: 1);

        var invalidLocationId = Guid.NewGuid();
        await this.SeedLocationAsync(invalidLocationId, code: "S3", label: "Zone S3");

        var invalidRunId = Guid.Parse("10000000-0000-0000-0000-00000000B555");
        var sessionId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-3);

        using (var scope = this._factory.Services.CreateScope())
        {
            var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
            await using var connection = connectionFactory.CreateConnection();
            await EnsureConnectionOpenAsync(connection);

            const string insertSessionSql = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc);";
            await connection.ExecuteAsync(insertSessionSql, new { Id = sessionId, Name = "Session invalide", StartedAtUtc = startedAt });

            await CountingRunSqlHelper.InsertAsync(
                connection,
                new CountingRunInsert(
                    invalidRunId,
                    sessionId,
                    invalidLocationId,
                    CountType: 1,
                    StartedAtUtc: startedAt,
                    CompletedAtUtc: null,
                    OperatorDisplayName: "bob.martin"));
        }

        var response = await this._client.GetAsync($"/api/locations?shopId={seed.ShopId:D}&countType=1");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<LocationResponse>>();
        Assert.NotNull(payload);

        var validLocation = payload!.Single(item => item.Code == "S1");
        Assert.Equal(seed.RunId, validLocation.ActiveRunId);
        Assert.Matches(ActiveRunIdRegex, validLocation.ActiveRunId!.Value.ToString());

        var invalidLocation = payload.Single(item => item.Code == "S3");
        Assert.True(invalidLocation.IsBusy);
        Assert.Null(invalidLocation.ActiveRunId);
    }

    [Fact]
    public async Task RestartInventoryForLocation_ClosesExistingRuns()
    {
        await this.ResetDatabaseAsync();
        var seed = await this.SeedDataAsync(countType: 1);

        var response = await this._client.PostAsync($"/api/inventories/{seed.BusyLocationId}/restart?countType=1", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = this._factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string query = "SELECT \"CompletedAtUtc\" FROM \"CountingRun\" WHERE \"Id\" = @RunId";
        var completedAtDt = await connection.ExecuteScalarAsync<DateTime?>(query, new { seed.RunId });

        DateTimeOffset? completedAt = null;
        if (completedAtDt is not null)
        {
            var completedAtDtValue = completedAtDt.Value;
            if (completedAtDtValue.Kind == DateTimeKind.Unspecified)
            {
                completedAtDtValue = DateTime.SpecifyKind(completedAtDtValue, DateTimeKind.Utc);
            }

            completedAt = new DateTimeOffset(completedAtDtValue, TimeSpan.Zero);
        }

        Assert.NotNull(completedAt);
    }

    [Fact]
    public async Task RestartInventoryForLocation_WithNoActiveRun_IsNoOp()
    {
        await this.ResetDatabaseAsync();
        var locationId = Guid.NewGuid();

        await this.SeedLocationAsync(locationId, code: "S3", label: "Zone S3");

        var response = await this._client.PostAsync($"/api/inventories/{locationId}/restart?countType=1", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetLocations_ReturnsJsonArrayWithExpectedShape()
    {
        await this.ResetDatabaseAsync();

        var locationId = Guid.NewGuid();
        var shopId = await this.SeedLocationAsync(locationId, code: "A1", label: "Allée 1");

        var response = await this._client.GetAsync($"/api/locations?shopId={shopId:D}");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);

        if (document.RootElement.GetArrayLength() > 0)
        {
            var first = document.RootElement[0];
            Assert.Equal(JsonValueKind.Object, first.ValueKind);
            Assert.True(first.TryGetProperty("id", out _));
            Assert.True(first.TryGetProperty("code", out _));
            Assert.True(first.TryGetProperty("label", out _));
            Assert.True(first.TryGetProperty("isBusy", out _));
        }
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = this._factory.Services.CreateScope();
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
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql);
    }

    private async Task<Guid> SeedLocationAsync(Guid id, string code, string label)
    {
        using var scope = this._factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var shopId = await EnsureDefaultShopAsync(connection);

        await InsertLocationAsync(connection, shopId, id, code, label);
        return shopId;
    }

    private async Task<SeedDataResult> SeedDataAsync(int countType)
    {
        using var scope = this._factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var shopId = await EnsureDefaultShopAsync(connection);
        var busyLocationId = Guid.NewGuid();
        var freeLocationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-7);

        const string insertLocationSql =
            "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\", \"ShopId\") VALUES (@Id, @Code, @Label, @ShopId);";
        await connection.ExecuteAsync(insertLocationSql, new { Id = busyLocationId, Code = "S1", Label = "Zone S1", ShopId = shopId });
        await connection.ExecuteAsync(insertLocationSql, new { Id = freeLocationId, Code = "S2", Label = "Zone S2", ShopId = shopId });

        const string insertSessionSql = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc);";
        await connection.ExecuteAsync(insertSessionSql, new { Id = sessionId, Name = "Session principale", StartedAtUtc = startedAt });

        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                runId,
                sessionId,
                busyLocationId,
                CountType: countType,
                StartedAtUtc: startedAt,
                CompletedAtUtc: null,
                OperatorDisplayName: "alice.durand"));

        return new SeedDataResult(shopId, busyLocationId, runId);
    }

    private readonly record struct SeedDataResult(Guid ShopId, Guid BusyLocationId, Guid RunId);

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

    private static Task<Guid> EnsureDefaultShopAsync(IDbConnection connection)
        => EnsureShopAsync(connection, "CinéBoutique Paris");

    private static async Task<Guid> EnsureShopAsync(IDbConnection connection, string name)
    {
        const string ensureShopSql =
            "INSERT INTO \"Shop\" (\"Name\") VALUES (@Name) ON CONFLICT DO NOTHING;";
        const string selectShopSql =
            "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name) LIMIT 1;";

        await connection.ExecuteAsync(ensureShopSql, new { Name = name });
        return await connection.ExecuteScalarAsync<Guid>(selectShopSql, new { Name = name });
    }

    private static Task InsertLocationAsync(IDbConnection connection, Guid shopId, Guid locationId, string code, string label)
    {
        const string insertLocationSql =
            "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\", \"ShopId\") VALUES (@Id, @Code, @Label, @ShopId);";
        return connection.ExecuteAsync(insertLocationSql, new { Id = locationId, Code = code, Label = label, ShopId = shopId });
    }

    private async Task<bool> HasOperatorDisplayNameColumnAsync()
    {
        using var scope = this._factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        return await CountingRunSqlHelper.HasOperatorDisplayNameAsync(connection);
    }

    private sealed record LocationResponse
    {
        public Guid Id { get; init; }

        public string Code { get; init; } = string.Empty;

        public string Label { get; init; } = string.Empty;

        public bool IsBusy { get; init; }

        public string? BusyBy { get; init; }

        public Guid? ActiveRunId { get; init; }

        public short? ActiveCountType { get; init; }

        public DateTimeOffset? ActiveStartedAtUtc { get; init; }
    }
}
