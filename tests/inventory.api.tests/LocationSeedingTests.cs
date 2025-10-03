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
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class LocationSeedingTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;

    private static readonly string[] ExpectedShopNames =
    {
        "CinéBoutique Bordeaux",
        "CinéBoutique Bruxelles",
        "CinéBoutique Marseille",
        "CinéBoutique Montpellier",
        "CinéBoutique Paris"
    };

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
                "SELECT \"Code\", \"Label\", \"ShopId\" FROM \"Location\" ORDER BY \"Code\";"))
            .ToList();

        var parisShopId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name) LIMIT 1;",
            new { Name = "CinéBoutique Paris" });

        Assert.Equal(39, locations.Count);

        var expectedCodes = BuildExpectedCodes();
        Assert.True(
            expectedCodes.SetEquals(locations.Select(location => location.Code)),
            "Les 39 codes attendus doivent être présents dans la base de données.");
        Assert.All(locations, location => Assert.Equal($"Zone {location.Code}", location.Label));
        Assert.All(locations, location => Assert.Equal(parisShopId, location.ShopId));
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

        var shopCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"Shop\";");
        Assert.Equal(5, shopCount);

        var duplicateShops = await connection.QueryAsync<string>(
            "SELECT LOWER(\"Name\") FROM \"Shop\" GROUP BY LOWER(\"Name\") HAVING COUNT(*) > 1;");
        Assert.Empty(duplicateShops);
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

    [Fact]
    public async Task SeedAsync_CreatesConfiguredShops()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var shops = (await connection.QueryAsync<ShopRow>(
                "SELECT \"Id\", \"Name\" FROM \"Shop\" ORDER BY \"Name\";"))
            .ToList();

        Assert.Equal(5, shops.Count);
        Assert.Equal(ExpectedShopNames, shops.Select(shop => shop.Name).ToArray());
    }

    [Fact]
    public async Task LocationsWithSameCode_AreAllowedAcrossDifferentShops()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var parisShopId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name);",
            new { Name = "CinéBoutique Paris" });
        var bordeauxShopId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name);",
            new { Name = "CinéBoutique Bordeaux" });

        const string code = "X1";

        await connection.ExecuteAsync(
            "INSERT INTO \"Location\" (\"Code\", \"Label\", \"ShopId\") VALUES (@Code, 'Test Paris', @ShopId);",
            new { Code = code, ShopId = parisShopId });

        await connection.ExecuteAsync(
            "INSERT INTO \"Location\" (\"Code\", \"Label\", \"ShopId\") VALUES (@Code, 'Test Bordeaux', @ShopId);",
            new { Code = code, ShopId = bordeauxShopId });

        var duplicateCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM \"Location\" WHERE \"Code\" = @Code;",
            new { Code = code });

        Assert.Equal(2, duplicateCount);
    }

    [Fact]
    public async Task DuplicateCodes_AreRejectedWithinSameShop()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var parisShopId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name);",
            new { Name = "CinéBoutique Paris" });

        const string baseCode = "x2";

        await connection.ExecuteAsync(
            "INSERT INTO \"Location\" (\"Code\", \"Label\", \"ShopId\") VALUES (@Code, 'Test 1', @ShopId);",
            new { Code = baseCode.ToUpperInvariant(), ShopId = parisShopId });

        var exception = await Assert.ThrowsAsync<PostgresException>(
            async () =>
                await connection.ExecuteAsync(
                    "INSERT INTO \"Location\" (\"Code\", \"Label\", \"ShopId\") VALUES (@Code, 'Test 2', @ShopId);",
                    new { Code = baseCode, ShopId = parisShopId }));

        Assert.Equal("23505", exception.SqlState);
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
TRUNCATE TABLE "Shop" RESTART IDENTITY CASCADE;
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instancié via la réflexion de Dapper lors du mapping des résultats.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instancié via la réflexion de Dapper lors du mapping des résultats.")]
    private sealed record LocationRow(string Code, string Label, Guid ShopId);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instancié via la réflexion de Dapper lors du mapping des résultats.")]
    private sealed record ShopRow(Guid Id, string Name);
}
