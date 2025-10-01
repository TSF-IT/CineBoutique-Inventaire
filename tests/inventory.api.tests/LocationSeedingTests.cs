#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
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
public sealed class LocationSeedingTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;

    public LocationSeedingTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString);
        await _factory.EnsureMigratedAsync();
        await ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SeedAsync_PopulatesAllConfiguredZones()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var locations = (await connection.QueryAsync<LocationRow>(
                "SELECT \"Code\", \"Label\" FROM \"Location\" ORDER BY \"Code\";"))
            .ToList();

        Assert.Equal(39, locations.Count);

        var expectedCodes = BuildExpectedCodes();
        Assert.True(
            expectedCodes.SetEquals(locations.Select(location => location.Code)),
            "Les 39 codes attendus doivent être présents dans la base de données.");
        Assert.All(locations, location => Assert.Equal($"Zone {location.Code}", location.Label));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"Location\";");
        Assert.Equal(39, count);

        var duplicateCodes = await connection.QueryAsync<string>(
            "SELECT \"Code\" FROM \"Location\" GROUP BY \"Code\" HAVING COUNT(*) > 1;");
        Assert.Empty(duplicateCodes);
    }

    [Fact]
    public async Task LocationsEndpoint_ReturnsSeededZones()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        using HttpClient client = _factory.CreateClient();
        var response = await client.GetAsync("/api/locations?countType=1");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<LocationListItemDto>>();
        Assert.NotNull(payload);

        var locations = payload!;
        Assert.Equal(39, locations.Count);

        var expectedCodes = BuildExpectedCodes();
        Assert.True(expectedCodes.SetEquals(locations.Select(item => item.Code)), "Tous les codes de zones doivent être présents.");
        Assert.All(locations, item => Assert.False(item.IsBusy));
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

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
TRUNCATE TABLE "Product" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql);
    }

    private static HashSet<string> BuildExpectedCodes()
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 1; index <= 20; index++)
        {
            codes.Add($"B{index}");
        }

        for (var index = 1; index <= 19; index++)
        {
            codes.Add($"S{index}");
        }

        return codes;
    }

    private sealed record LocationRow(string Code, string Label);
}
