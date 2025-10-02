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
public sealed class SeedingTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;

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
        await _factory.EnsureMigratedAsync();
        await ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Seed_Shops_Idempotent()
    {
        await ResetDatabaseAsync();

        await SeedAsync();
        await SeedAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        var shops = await connection.QueryAsync<(Guid Id, string Name)>("SELECT \"Id\", \"Name\" FROM \"Shop\";");

        Assert.Equal(5, shops.Count());
        Assert.Equal(5, shops.Select(s => s.Name.ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public async Task Seed_Zones_Demo_NonParis()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"SELECT s.\"Name\" AS ShopName, l.\"Code\" AS Code
FROM \"Location\" l
JOIN \"Shop\" s ON s.\"Id\" = l.\"ShopId\"
WHERE s.\"Name\" <> 'CinéBoutique Paris'";

        var rows = await connection.QueryAsync<(string ShopName, string Code)>(sql);
        var grouped = rows.GroupBy(r => r.ShopName);

        foreach (var group in grouped)
        {
            var codes = group.Select(r => r.Code).OrderBy(c => c).ToArray();
            Assert.Equal(new[] { "A", "B", "C", "D", "E" }, codes);
        }
    }

    [Fact]
    public async Task ShopUser_Mandatory_Administrateur()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"SELECT s.\"Name\" AS ShopName, su.\"DisplayName\" AS DisplayName, su.\"IsAdmin\" AS IsAdmin
FROM \"ShopUser\" su
JOIN \"Shop\" s ON s.\"Id\" = su.\"ShopId\"
WHERE lower(su.\"Login\") = 'administrateur';";

        var admins = await connection.QueryAsync<(string ShopName, string DisplayName, bool IsAdmin)>(sql);
        Assert.Equal(5, admins.Count());

        foreach (var admin in admins)
        {
            Assert.Equal("Administrateur", admin.DisplayName);
            Assert.True(admin.IsAdmin);
        }
    }

    [Fact]
    public async Task ShopUser_Paris_Has_6_Total()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        var count = await CountUsersAsync("CinéBoutique Paris");
        Assert.Equal(6, count);
    }

    [Fact]
    public async Task ShopUser_Others_Have_5_Total()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        foreach (var shop in new[] { "CinéBoutique Bordeaux", "CinéBoutique Montpellier", "CinéBoutique Marseille", "CinéBoutique Bruxelles" })
        {
            var count = await CountUsersAsync(shop);
            Assert.Equal(5, count);
        }
    }

    [Fact]
    public async Task ShopUser_Login_Unique_Per_Shop()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        var parisId = await connection.ExecuteScalarAsync<Guid>("SELECT \"Id\" FROM \"Shop\" WHERE \"Name\" = 'CinéBoutique Paris' LIMIT 1;");

        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await connection.ExecuteAsync(
                "INSERT INTO \"ShopUser\" (\"ShopId\", \"Login\", \"DisplayName\", \"IsAdmin\") VALUES (@ShopId, 'administrateur', 'Dup', FALSE);",
                new { ShopId = parisId });
        });
    }

    [Fact]
    public async Task Location_Code_Unique_Per_Shop()
    {
        await ResetDatabaseAsync();
        await SeedAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        var parisId = await connection.ExecuteScalarAsync<Guid>("SELECT \"Id\" FROM \"Shop\" WHERE \"Name\" = 'CinéBoutique Paris' LIMIT 1;");
        var bordeauxId = await connection.ExecuteScalarAsync<Guid>("SELECT \"Id\" FROM \"Shop\" WHERE \"Name\" = 'CinéBoutique Bordeaux' LIMIT 1;");

        await connection.ExecuteAsync("INSERT INTO \"Location\" (\"ShopId\", \"Code\", \"Label\") VALUES (@ShopId, 'Z1', 'Zone Z1');", new { ShopId = parisId });
        await connection.ExecuteAsync("INSERT INTO \"Location\" (\"ShopId\", \"Code\", \"Label\") VALUES (@ShopId, 'Z1', 'Zone Z1');", new { ShopId = bordeauxId });

        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await connection.ExecuteAsync("INSERT INTO \"Location\" (\"ShopId\", \"Code\", \"Label\") VALUES (@ShopId, 'Z1', 'Autre Z1');", new { ShopId = parisId });
        });
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

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

        await connection.ExecuteAsync(cleanupSql);
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();
    }

    private async Task<int> CountUsersAsync(string shopName)
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        const string sql = @"SELECT COUNT(*)::int
FROM \"ShopUser\" su
JOIN \"Shop\" s ON s.\"Id\" = su.\"ShopId\"
WHERE s.\"Name\" = @ShopName;";

        return await connection.ExecuteScalarAsync<int>(sql, new { ShopName = shopName });
    }
}
