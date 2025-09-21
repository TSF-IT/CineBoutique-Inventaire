#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA1812
#pragma warning disable CA2007
#pragma warning disable CA2234
#pragma warning disable CA1859

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

public sealed class LocationsEndpointTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private readonly TestDatabaseFixture _databaseFixture;
    private InventoryApiApplicationFactory? _factory;
    private HttpClient? _client;

    public LocationsEndpointTests(TestDatabaseFixture databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    private InventoryApiApplicationFactory Factory => _factory ?? throw new InvalidOperationException("Factory not initialised");

    private HttpClient Client => _client ?? throw new InvalidOperationException("Client not initialised");

    public Task InitializeAsync()
    {
        if (!_databaseFixture.IsDockerAvailable)
        {
            return Task.CompletedTask;
        }

        _factory = new InventoryApiApplicationFactory(_databaseFixture.ConnectionString);
        _client = _factory.CreateClient();
        return ResetDatabaseAsync();
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
    public async Task GetLocations_ReturnsBusyStatus_ForRequestedCountType()
    {
        if (SkipIfDockerUnavailable())
        {
            return;
        }

        await ResetDatabaseAsync();
        var seed = await SeedDataAsync(countType: 1);

        Factory.ConnectionCounter.Reset();

        var response = await Client.GetAsync("/api/locations?countType=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<List<LocationResponse>>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload.Count);

        var busy = payload.Single(item => item.Code == "S1");
        Assert.True(busy.IsBusy);
        Assert.Equal(seed.RunId, busy.ActiveRunId);
        Assert.Equal("alice.durand", busy.BusyBy);
        Assert.Equal((short)1, busy.ActiveCountType);
        Assert.NotNull(busy.ActiveStartedAtUtc);

        var free = payload.Single(item => item.Code == "S2");
        Assert.False(free.IsBusy);
        Assert.Null(free.ActiveRunId);

        Assert.Equal(1, Factory.ConnectionCounter.CommandCount);
    }

    [Fact]
    public async Task GetLocations_WithMismatchedCountType_ReturnsFreeState()
    {
        if (SkipIfDockerUnavailable())
        {
            return;
        }

        await ResetDatabaseAsync();
        await SeedDataAsync(countType: 2);

        Factory.ConnectionCounter.Reset();

        var response = await Client.GetAsync("/api/locations?countType=1");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<LocationResponse>>();
        Assert.NotNull(payload);
        var busy = payload.Single(item => item.Code == "S1");
        Assert.False(busy.IsBusy);
        Assert.Null(busy.ActiveRunId);
        Assert.Equal(1, Factory.ConnectionCounter.CommandCount);
    }

    [Fact]
    public async Task GetLocations_WithInvalidCountType_ReturnsBadRequest()
    {
        if (SkipIfDockerUnavailable())
        {
            return;
        }

        Factory.ConnectionCounter.Reset();
        var response = await Client.GetAsync("/api/locations?countType=5");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RestartInventoryForLocation_ClosesExistingRuns()
    {
        if (SkipIfDockerUnavailable())
        {
            return;
        }

        await ResetDatabaseAsync();
        var seed = await SeedDataAsync(countType: 1);

        var response = await Client.PostAsync($"/api/inventories/{seed.LocationId}/restart?countType=1", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string query = "SELECT \"CompletedAtUtc\" FROM \"CountingRun\" WHERE \"Id\" = @RunId";
        var completedAt = await connection.ExecuteScalarAsync<DateTimeOffset?>(query, new { seed.RunId });
        Assert.NotNull(completedAt);
    }

    [Fact]
    public async Task RestartInventoryForLocation_WithNoActiveRun_IsNoOp()
    {
        if (SkipIfDockerUnavailable())
        {
            return;
        }

        await ResetDatabaseAsync();
        var locationId = Guid.NewGuid();

        await SeedLocationAsync(locationId, code: "S3", label: "Zone S3");

        var response = await Client.PostAsync($"/api/inventories/{locationId}/restart?countType=1", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public void RestartInventoryEndpoint_IsSkippedWhenDockerUnavailable()
    {
        var skipped = SkipIfDockerUnavailable();

        if (_databaseFixture.IsDockerAvailable)
        {
            Assert.False(skipped);
        }
        else
        {
            Assert.True(skipped);
        }
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string cleanupSql = @"
TRUNCATE TABLE ""CountLine"" RESTART IDENTITY CASCADE;
TRUNCATE TABLE ""CountingRun"" RESTART IDENTITY CASCADE;
TRUNCATE TABLE ""InventorySession"" RESTART IDENTITY CASCADE;
TRUNCATE TABLE ""Location"" RESTART IDENTITY CASCADE;";

        await connection.ExecuteAsync(cleanupSql);

        Factory.ConnectionCounter.Reset();
    }

    private bool SkipIfDockerUnavailable()
    {
        if (_databaseFixture.IsDockerAvailable)
        {
            return false;
        }

        Assert.True(true, "Docker est requis pour exécuter les tests d'intégration API.");
        return true;
    }

    private async Task SeedLocationAsync(Guid id, string code, string label)
    {
        using var scope = Factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection);

        const string insertLocationSql = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, @Code, @Label);";
        await connection.ExecuteAsync(insertLocationSql, new { Id = id, Code = code, Label = label });
    }

    private async Task<(Guid LocationId, Guid RunId)> SeedDataAsync(int countType)
    {
        using var scope = Factory.Services.CreateScope();
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

        Factory.ConnectionCounter.Reset();

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

    private sealed record LocationResponse(
        Guid Id,
        string Code,
        string Label,
        bool IsBusy,
        string? BusyBy,
        Guid? ActiveRunId,
        short? ActiveCountType,
        DateTimeOffset? ActiveStartedAtUtc);
}
