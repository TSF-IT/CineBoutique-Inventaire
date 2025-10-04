#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.Seeding;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class ShopUserSeedingTests : IAsyncLifetime
{
    private const string ParisShopName = "CinéBoutique Paris";

    private static readonly string[] ExpectedParisDisplayNames =
    {
        "Administrateur",
        "Utilisateur Paris",
        "Utilisateur Paris 1",
        "Utilisateur Paris 2",
        "Utilisateur Paris 3"
    };

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedNonParisDisplayNames =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["CinéBoutique Bordeaux"] = new[]
            {
                "Administrateur",
                "Utilisateur Bordeaux 1",
                "Utilisateur Bordeaux 2",
                "Utilisateur Bordeaux 3"
            },
            ["CinéBoutique Montpellier"] = new[]
            {
                "Administrateur",
                "Utilisateur Montpellier 1",
                "Utilisateur Montpellier 2",
                "Utilisateur Montpellier 3"
            },
            ["CinéBoutique Marseille"] = new[]
            {
                "Administrateur",
                "Utilisateur Marseille 1",
                "Utilisateur Marseille 2",
                "Utilisateur Marseille 3"
            },
            ["CinéBoutique Bruxelles"] = new[]
            {
                "Administrateur",
                "Utilisateur Bruxelles 1",
                "Utilisateur Bruxelles 2",
                "Utilisateur Bruxelles 3"
            }
        };

    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;

    public ShopUserSeedingTests(PostgresTestContainerFixture pg)
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
    public async Task SeedAsync_ParisShopContainsExpectedActiveUsers()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        await RunSeederAsync(scope.ServiceProvider);

        var parisShopId = await GetShopIdAsync(scope.ServiceProvider, ParisShopName);
        var users = await GetUsersByShopAsync(scope.ServiceProvider, parisShopId);

        var active = users.Where(user => !user.Disabled).OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(ExpectedParisDisplayNames.Length, active.Count);
        Assert.Equal(ExpectedParisDisplayNames, active.Select(user => user.DisplayName).ToArray());

        var admin = Assert.Single(active.Where(user => user.IsAdmin));
        Assert.Equal("Administrateur", admin.DisplayName);
    }

    [Fact]
    public async Task SeedAsync_NonParisShopsContainExpectedActiveUsers()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        await RunSeederAsync(scope.ServiceProvider);

        foreach (var expected in ExpectedNonParisDisplayNames)
        {
            var shopId = await GetShopIdAsync(scope.ServiceProvider, expected.Key);
            var users = await GetUsersByShopAsync(scope.ServiceProvider, shopId);
            var active = users.Where(user => !user.Disabled).OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.Equal(expected.Value.Length, active.Count);
            Assert.Equal(expected.Value, active.Select(user => user.DisplayName).ToArray());

            var admin = Assert.Single(active.Where(user => user.IsAdmin));
            Assert.Equal("Administrateur", admin.DisplayName);
        }
    }

    [Fact]
    public async Task SeedAsync_DisablesUnexpectedUsersAndEnforcesSingleAdminPerShop()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        await RunSeederAsync(scope.ServiceProvider);

        var services = scope.ServiceProvider;
        var shopId = await GetShopIdAsync(services, "CinéBoutique Bordeaux");

        var rogueUserId = await InsertUserAsync(
            services,
            shopId,
            login: "intrus-bordeaux",
            displayName: "Intrus Bordeaux",
            isAdmin: false,
            disabled: false);

        var rogueAdminId = await InsertUserAsync(
            services,
            shopId,
            login: "super-admin",
            displayName: "Super Admin",
            isAdmin: true,
            disabled: false);

        await RunSeederAsync(services);

        var users = await GetUsersByShopAsync(services, shopId);

        var admin = Assert.Single(users.Where(user => user.IsAdmin && !user.Disabled));
        Assert.Equal("Administrateur", admin.DisplayName);

        var rogueUser = Assert.Single(users.Where(user => user.Id == rogueUserId));
        Assert.True(rogueUser.Disabled);
        Assert.False(rogueUser.IsAdmin);

        var rogueAdmin = Assert.Single(users.Where(user => user.Id == rogueAdminId));
        Assert.True(rogueAdmin.Disabled);
        Assert.False(rogueAdmin.IsAdmin);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        await RunSeederAsync(scope.ServiceProvider);

        var snapshot = await SnapshotUsersAsync(scope.ServiceProvider);

        await RunSeederAsync(scope.ServiceProvider);

        var secondSnapshot = await SnapshotUsersAsync(scope.ServiceProvider);

        Assert.Equal(snapshot.Count, secondSnapshot.Count);

        foreach (var (shopId, first) in snapshot)
        {
            Assert.True(secondSnapshot.TryGetValue(shopId, out var second));
            Assert.Equal(first.Count, second.Count);

            foreach (var user in first.OrderBy(user => user.Id))
            {
                var matching = Assert.Single(second.Where(candidate => candidate.Id == user.Id));
                Assert.Equal(user.DisplayName, matching.DisplayName);
                Assert.Equal(user.IsAdmin, matching.IsAdmin);
                Assert.Equal(user.Disabled, matching.Disabled);
            }
        }
    }

    private static async Task RunSeederAsync(IServiceProvider services)
    {
        var seeder = services.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();
    }

    private static async Task<Guid> GetShopIdAsync(IServiceProvider services, string shopName)
    {
        var connectionFactory = services.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name) LIMIT 1;",
            new { Name = shopName });
    }

    private static async Task<IReadOnlyList<ShopUserRow>> GetUsersByShopAsync(IServiceProvider services, Guid shopId)
    {
        var connectionFactory = services.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var rows = await connection.QueryAsync<ShopUserRow>(
            """
            SELECT "Id", "ShopId", "DisplayName", "IsAdmin", "Disabled"
            FROM "ShopUser"
            WHERE "ShopId" = @ShopId
            ORDER BY "DisplayName";
            """,
            new { ShopId = shopId });

        return rows.ToList();
    }

    private static async Task<IDictionary<Guid, List<ShopUserRow>>> SnapshotUsersAsync(IServiceProvider services)
    {
        var connectionFactory = services.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var rows = await connection.QueryAsync<ShopUserRow>(
            """
            SELECT "Id", "ShopId", "DisplayName", "IsAdmin", "Disabled"
            FROM "ShopUser";
            """ );

        return rows
            .GroupBy(row => row.ShopId)
            .ToDictionary(group => group.Key, group => group.OrderBy(user => user.Id).ToList());
    }

    private static async Task<Guid> InsertUserAsync(
        IServiceProvider services,
        Guid shopId,
        string login,
        string displayName,
        bool isAdmin,
        bool disabled)
    {
        var connectionFactory = services.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO "ShopUser" ("Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Secret_Hash", "Disabled")
            VALUES (@Id, @ShopId, @Login, @DisplayName, @IsAdmin, '', @Disabled);
            """,
            new
            {
                Id = id,
                ShopId = shopId,
                Login = login,
                DisplayName = displayName,
                IsAdmin = isAdmin,
                Disabled = disabled
            });

        return id;
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
TRUNCATE TABLE "ShopUser" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Shop" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Product" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql);
    }

    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated via Dapper materializer.")]
    private sealed record ShopUserRow(Guid Id, Guid ShopId, string DisplayName, bool IsAdmin, bool Disabled);
}
