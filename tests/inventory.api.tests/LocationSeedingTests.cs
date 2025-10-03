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

    private const string ParisShopName = "CinéBoutique Paris";

    private static readonly string[] ExpectedShopNames =
    {
        "CinéBoutique Bordeaux",
        "CinéBoutique Bruxelles",
        "CinéBoutique Marseille",
        "CinéBoutique Montpellier",
        ParisShopName
    };

    private static readonly HashSet<string> ExpectedParisCodes = BuildExpectedParisCodes();

    private static readonly HashSet<string> ExpectedDemoCodes =
        new(new[] { "A", "B", "C", "D", "E" }, StringComparer.OrdinalIgnoreCase);

    private static readonly int ExpectedTotalLocationCount =
        ExpectedParisCodes.Count + (ExpectedShopNames.Length - 1) * ExpectedDemoCodes.Count;

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

        var shops = (await connection.QueryAsync<ShopRow>(
                "SELECT \"Id\", \"Name\" FROM \"Shop\";"))
            .ToList();

        var shopIdByName = shops.ToDictionary(shop => shop.Name, shop => shop.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(ExpectedTotalLocationCount, locations.Count);

        Assert.True(
            shopIdByName.TryGetValue(ParisShopName, out var parisShopId),
            "Le magasin de Paris doit être présent dans la base.");

        var parisLocations = locations.Where(location => location.ShopId == parisShopId).ToList();
        Assert.Equal(ExpectedParisCodes.Count, parisLocations.Count);

        var actualParisCodes = new HashSet<string>(
            parisLocations.Select(location => location.Code),
            StringComparer.OrdinalIgnoreCase);
        Assert.True(
            ExpectedParisCodes.SetEquals(actualParisCodes),
            "Les zones attendues doivent être présentes pour le magasin de Paris.");

        Assert.All(
            locations,
            location => Assert.Equal($"Zone {location.Code.ToUpperInvariant()}", location.Label));

        AssertNonParisShopsHaveDemoLocations(locations, shopIdByName);
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
        Assert.Equal(ExpectedTotalLocationCount, count);

        var duplicateCodes = await connection.QueryAsync<int>(
            "SELECT 1 FROM \"Location\" GROUP BY \"ShopId\", UPPER(\"Code\") HAVING COUNT(*) > 1;");
        Assert.Empty(duplicateCodes);

        var shopCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"Shop\";");
        Assert.Equal(5, shopCount);

        var duplicateShops = await connection.QueryAsync<string>(
            "SELECT LOWER(\"Name\") FROM \"Shop\" GROUP BY LOWER(\"Name\") HAVING COUNT(*) > 1;");
        Assert.Empty(duplicateShops);

        var locations = (await connection.QueryAsync<LocationRow>(
                "SELECT \"Code\", \"Label\", \"ShopId\" FROM \"Location\";"))
            .ToList();
        var shops = (await connection.QueryAsync<ShopRow>(
                "SELECT \"Id\", \"Name\" FROM \"Shop\";"))
            .ToList();
        var shopIdByName = shops.ToDictionary(shop => shop.Name, shop => shop.Id, StringComparer.OrdinalIgnoreCase);

        AssertNonParisShopsHaveDemoLocations(locations, shopIdByName);
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
        Assert.Equal(ExpectedTotalLocationCount, locations.Count);

        var expectedCodes = BuildExpectedUniqueCodes();
        Assert.True(
            expectedCodes.SetEquals(locations.Select(item => item.Code)),
            "Tous les codes de zones doivent être présents.");
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

    private static HashSet<string> BuildExpectedParisCodes()
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

    private static HashSet<string> BuildExpectedUniqueCodes()
    {
        var codes = new HashSet<string>(ExpectedParisCodes, StringComparer.OrdinalIgnoreCase);
        codes.UnionWith(ExpectedDemoCodes);
        return codes;
    }

    private static void AssertNonParisShopsHaveDemoLocations(
        IEnumerable<LocationRow> locations,
        IReadOnlyDictionary<string, Guid> shopIdByName)
    {
        foreach (var shopName in ExpectedShopNames.Where(name => !string.Equals(name, ParisShopName, StringComparison.OrdinalIgnoreCase)))
        {
            Assert.True(
                shopIdByName.TryGetValue(shopName, out var shopId),
                $"Le magasin {shopName} doit être présent dans la base.");

            var shopCodes = new HashSet<string>(
                locations.Where(location => location.ShopId == shopId).Select(location => location.Code),
                StringComparer.OrdinalIgnoreCase);

            Assert.Equal(ExpectedDemoCodes.Count, shopCodes.Count);
            Assert.True(
                ExpectedDemoCodes.SetEquals(shopCodes),
                $"Le magasin {shopName} doit contenir les zones de démonstration A..E.");
        }
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
