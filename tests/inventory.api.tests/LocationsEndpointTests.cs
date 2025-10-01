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
    public async Task GetLocations_ReturnsBusyStatus_ForRequestedCountType()
    {
        await this.ResetDatabaseAsync();
        var seed = await this.SeedDataAsync(countType: 1);

        var response = await this._client.GetAsync("/api/locations?countType=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<List<LocationResponse>>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Count);

        var busy = payload.Single(item => item.Code == "S1");
        Assert.True(busy.IsBusy);
        Assert.Equal(seed.RunId, busy.ActiveRunId);
        Assert.Equal("alice.durand", busy.BusyBy);
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
        await this.SeedDataAsync(countType: 2);

        var response = await this._client.GetAsync("/api/locations?countType=1");
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
        var response = await this._client.GetAsync("/api/locations?countType=5");
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

            const string insertRunSql = @"
INSERT INTO ""CountingRun"" (""Id"", ""InventorySessionId"", ""LocationId"", ""StartedAtUtc"", ""CountType"", ""OperatorDisplayName"")
VALUES (@Id, @SessionId, @LocationId, @StartedAtUtc, @CountType, @Operator);";

            await connection.ExecuteAsync(
                insertRunSql,
                new
                {
                    Id = invalidRunId,
                    SessionId = sessionId,
                    LocationId = invalidLocationId,
                    StartedAtUtc = startedAt,
                    CountType = 1,
                    Operator = "bob.martin"
                });
        }

        var response = await this._client.GetAsync("/api/locations?countType=1");
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

        var response = await this._client.PostAsync($"/api/inventories/{seed.LocationId}/restart?countType=1", null);
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
        await this.SeedLocationAsync(locationId, code: "A1", label: "Allée 1");

        var response = await this._client.GetAsync("/api/locations");
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
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql);
    }

    private async Task SeedLocationAsync(Guid id, string code, string label)
    {
        using var scope = this._factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string insertLocationSql = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, @Code, @Label);";
        await connection.ExecuteAsync(insertLocationSql, new { Id = id, Code = code, Label = label });
    }

    private async Task<(Guid LocationId, Guid RunId)> SeedDataAsync(int countType)
    {
        using var scope = this._factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        var busyLocationId = Guid.NewGuid();
        var freeLocationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-7);

        const string insertLocationSql = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, @Code, @Label);";
        await connection.ExecuteAsync(insertLocationSql, new { Id = busyLocationId, Code = "S1", Label = "Zone S1" });
        await connection.ExecuteAsync(insertLocationSql, new { Id = freeLocationId, Code = "S2", Label = "Zone S2" });

        const string insertSessionSql = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc);";
        await connection.ExecuteAsync(insertSessionSql, new { Id = sessionId, Name = "Session principale", StartedAtUtc = startedAt });

        const string insertRunSql = @"
INSERT INTO ""CountingRun"" (""Id"", ""InventorySessionId"", ""LocationId"", ""StartedAtUtc"", ""CountType"", ""OperatorDisplayName"")
VALUES (@Id, @SessionId, @LocationId, @StartedAtUtc, @CountType, @Operator);";

        await connection.ExecuteAsync(
            insertRunSql,
            new
            {
                Id = runId,
                SessionId = sessionId,
                LocationId = busyLocationId,
                StartedAtUtc = startedAt,
                CountType = countType,
                Operator = "alice.durand"
            });

        return (busyLocationId, runId);
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
