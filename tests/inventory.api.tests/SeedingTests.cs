using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.Seeding;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class SeedingTests : IAsyncLifetime, IDisposable
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory? _factory;
    private static readonly string[] DemoZoneCodes = { "A", "B", "C", "D", "E" };

    public SeedingTests(PostgresTestContainerFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Authentication:Issuer"] = "CineBoutique.Inventory",
            ["Authentication:Audience"] = "CineBoutique.Inventory",
            ["Authentication:Secret"] = "ChangeMe-Secret-Key-For-Inventory-Api-123",
            ["Authentication:TokenLifetimeMinutes"] = "30"
        };

        _factory = new InventoryApiApplicationFactory(_pg.ConnectionString, configuration);
        await _factory.EnsureMigratedAsync().ConfigureAwait(true);
        await ResetDatabaseAsync().ConfigureAwait(true);
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SeedShopsIsIdempotentAsync()
    {
        await ResetDatabaseAsync().ConfigureAwait(true);

        await SeedAsync().ConfigureAwait(true);
        await SeedAsync().ConfigureAwait(true);

        using var scope = GetFactory().Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        #pragma warning disable CA2007 // DisposeAsync n'expose pas ConfigureAwait
        await using var connection = connectionFactory.CreateConnection();
        #pragma warning restore CA2007
        await connection.OpenAsync().ConfigureAwait(true);

        var shops = await connection.QueryAsync<(Guid Id, string Name)>("SELECT \"Id\", \"Name\" FROM \"Shop\";").ConfigureAwait(true);

        Assert.Equal(5, shops.Count());
        Assert.Equal(5, shops.Select(s => s.Name.ToUpperInvariant()).Distinct().Count());
    }

    [Fact]
    public async Task SeedZonesDemoNonParisAsync()
    {
        await ResetDatabaseAsync().ConfigureAwait(true);
        await SeedAsync().ConfigureAwait(true);

        using var scope = GetFactory().Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        #pragma warning disable CA2007 // DisposeAsync n'expose pas ConfigureAwait
        await using var connection = connectionFactory.CreateConnection();
        #pragma warning restore CA2007
        await connection.OpenAsync().ConfigureAwait(true);

        const string sql = """
SELECT s."Name" AS ShopName, l."Code" AS Code
FROM "Location" l
JOIN "Shop" s ON s."Id" = l."ShopId"
WHERE s."Name" <> 'CinéBoutique Paris'
""";

        var rows = await connection.QueryAsync<(string ShopName, string Code)>(sql).ConfigureAwait(true);
        var grouped = rows.GroupBy(r => r.ShopName);

        foreach (var group in grouped)
        {
            var codes = group.Select(r => r.Code).OrderBy(c => c).ToArray();
            Assert.Equal(DemoZoneCodes, codes);
        }
    }

    [Fact]
    public async Task ShopUserMandatoryAdministrateurAsync()
    {
        await ResetDatabaseAsync().ConfigureAwait(true);
        await SeedAsync().ConfigureAwait(true);

        using var scope = GetFactory().Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        #pragma warning disable CA2007 // DisposeAsync n'expose pas ConfigureAwait
        await using var connection = connectionFactory.CreateConnection();
        #pragma warning restore CA2007
        await connection.OpenAsync().ConfigureAwait(true);

        const string sql = """
SELECT s."Name" AS ShopName, su."DisplayName" AS DisplayName, su."IsAdmin" AS IsAdmin
FROM "ShopUser" su
JOIN "Shop" s ON s."Id" = su."ShopId"
WHERE lower(su."Login") = 'administrateur';
""";

        var admins = await connection.QueryAsync<(string ShopName, string DisplayName, bool IsAdmin)>(sql).ConfigureAwait(true);
        Assert.Equal(5, admins.Count());

        foreach (var admin in admins)
        {
            Assert.Equal("Administrateur", admin.DisplayName);
            Assert.True(admin.IsAdmin);
        }
    }

    [Fact]
    public async Task ShopUserParisHasSixTotalAsync()
    {
        await ResetDatabaseAsync().ConfigureAwait(true);
        await SeedAsync().ConfigureAwait(true);

        var count = await CountUsersAsync("CinéBoutique Paris").ConfigureAwait(true);
        Assert.Equal(6, count);
    }

    [Fact]
    public async Task ShopUserOthersHaveFiveTotalAsync()
    {
        await ResetDatabaseAsync().ConfigureAwait(true);
        await SeedAsync().ConfigureAwait(true);

        var shops = new[]
        {
            "CinéBoutique Bordeaux",
            "CinéBoutique Montpellier",
            "CinéBoutique Marseille",
            "CinéBoutique Bruxelles"
        };

        foreach (var shop in shops)
        {
            var count = await CountUsersAsync(shop).ConfigureAwait(true);
            Assert.Equal(5, count);
        }
    }

    [Fact]
    public async Task ShopUserLoginUniquePerShopAsync()
    {
        await ResetDatabaseAsync().ConfigureAwait(true);
        await SeedAsync().ConfigureAwait(true);

        using var scope = GetFactory().Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        #pragma warning disable CA2007 // DisposeAsync n'expose pas ConfigureAwait
        await using var connection = connectionFactory.CreateConnection();
        #pragma warning restore CA2007
        await connection.OpenAsync().ConfigureAwait(true);

        var parisId = await connection.ExecuteScalarAsync<Guid>("SELECT \"Id\" FROM \"Shop\" WHERE \"Name\" = 'CinéBoutique Paris' LIMIT 1;").ConfigureAwait(true);

        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await connection.ExecuteAsync(
                "INSERT INTO \"ShopUser\" (\"ShopId\", \"Login\", \"DisplayName\", \"IsAdmin\") VALUES (@ShopId, 'administrateur', 'Dup', FALSE);",
                new { ShopId = parisId }).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task LocationCodeUniquePerShopAsync()
    {
        await ResetDatabaseAsync().ConfigureAwait(true);
        await SeedAsync().ConfigureAwait(true);

        using var scope = GetFactory().Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        #pragma warning disable CA2007 // DisposeAsync n'expose pas ConfigureAwait
        await using var connection = connectionFactory.CreateConnection();
        #pragma warning restore CA2007
        await connection.OpenAsync().ConfigureAwait(true);

        var parisId = await connection.ExecuteScalarAsync<Guid>("SELECT \"Id\" FROM \"Shop\" WHERE \"Name\" = 'CinéBoutique Paris' LIMIT 1;").ConfigureAwait(true);
        var bordeauxId = await connection.ExecuteScalarAsync<Guid>("SELECT \"Id\" FROM \"Shop\" WHERE \"Name\" = 'CinéBoutique Bordeaux' LIMIT 1;").ConfigureAwait(true);

        await connection.ExecuteAsync("INSERT INTO \"Location\" (\"ShopId\", \"Code\", \"Label\") VALUES (@ShopId, 'Z1', 'Zone Z1');", new { ShopId = parisId }).ConfigureAwait(true);
        await connection.ExecuteAsync("INSERT INTO \"Location\" (\"ShopId\", \"Code\", \"Label\") VALUES (@ShopId, 'Z1', 'Zone Z1');", new { ShopId = bordeauxId }).ConfigureAwait(true);

        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await connection.ExecuteAsync("INSERT INTO \"Location\" (\"ShopId\", \"Code\", \"Label\") VALUES (@ShopId, 'Z1', 'Autre Z1');", new { ShopId = parisId }).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = GetFactory().Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        #pragma warning disable CA2007 // DisposeAsync n'expose pas ConfigureAwait
        await using var connection = connectionFactory.CreateConnection();
        #pragma warning restore CA2007
        await connection.OpenAsync().ConfigureAwait(true);

        const string cleanupSql = """
DO $do$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'Audit') THEN
        EXECUTE 'TRUNCATE TABLE "Audit" RESTART IDENTITY CASCADE;';
    END IF;
END $do$;

TRUNCATE TABLE "CountLine" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Conflict" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "CountingRun" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "InventorySession" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Product" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Location" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "ShopUser" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Shop" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql).ConfigureAwait(true);
    }

    private async Task SeedAsync()
    {
        using var scope = GetFactory().Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync().ConfigureAwait(true);
    }

    private async Task<int> CountUsersAsync(string shopName)
    {
        using var scope = GetFactory().Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        #pragma warning disable CA2007 // DisposeAsync n'expose pas ConfigureAwait
        await using var connection = connectionFactory.CreateConnection();
        #pragma warning restore CA2007
        await connection.OpenAsync().ConfigureAwait(true);

        const string sql = """
SELECT COUNT(*)::int
FROM "ShopUser" su
JOIN "Shop" s ON s."Id" = su."ShopId"
WHERE s."Name" = @ShopName;
""";

        return await connection.ExecuteScalarAsync<int>(sql, new { ShopName = shopName }).ConfigureAwait(true);
    }

    private InventoryApiApplicationFactory GetFactory()
    {
        return _factory ?? throw new InvalidOperationException("Factory not initialized.");
    }
}
