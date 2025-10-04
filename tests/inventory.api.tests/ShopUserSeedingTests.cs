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
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class ShopUserSeedingTests : IAsyncLifetime
{
    private static readonly IReadOnlyDictionary<string, int> ExpectedActiveUserCountByShop = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["CinéBoutique Paris"] = 6,
        ["CinéBoutique Bordeaux"] = 4,
        ["CinéBoutique Montpellier"] = 4,
        ["CinéBoutique Marseille"] = 4,
        ["CinéBoutique Bruxelles"] = 4
    };

    private static readonly string[] NonParisExpectedDisplayNames =
    {
        "Administrateur",
        "Utilisateur 1",
        "Utilisateur 2",
        "Utilisateur 3"
    };

    private static readonly IReadOnlyList<ParisUserSeed> ParisUsersWithArnaud = new List<ParisUserSeed>
    {
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "arnaud.paris", "Arnaud", false, false),
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "celine.paris", "Céline", false, false),
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "david.paris", "David", false, false),
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"), "eva.paris", "Eva", false, false),
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005"), "francois.paris", "François", false, false),
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006"), "georges.paris", "Georges", true, false)
    };

    private static readonly IReadOnlyList<ParisUserSeed> ParisUsersWithoutArnaud = new List<ParisUserSeed>
    {
        new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), "alice.paris", "Alice", false, false),
        new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "bruno.paris", "Bruno", false, false),
        new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003"), "camille.paris", "Camille", false, false),
        new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004"), "dorian.paris", "Dorian", false, false),
        new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005"), "eric.paris", "Eric", false, false),
        new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000006"), "florence.paris", "Florence", true, false)
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
    public async Task SeedAsync_ParisAdminIsArnaudWhenPresent()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var parisShopId = await SeedParisUsersAsync(scope.ServiceProvider, ParisUsersWithArnaud);

        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var parisUsers = (await connection.QueryAsync<ShopUserRow>(
                """
                SELECT "Id", "DisplayName", "IsAdmin", "Disabled"
                FROM "ShopUser"
                WHERE "ShopId" = @ShopId
                ORDER BY "DisplayName";
                """,
                new { ShopId = parisShopId }))
            .ToList();

        Assert.Equal(ParisUsersWithArnaud.Count, parisUsers.Count);
        var admin = Assert.Single(parisUsers.Where(user => user.IsAdmin && !user.Disabled));
        Assert.Equal("Arnaud", admin.DisplayName);
        Assert.All(parisUsers.Where(user => !string.Equals(user.DisplayName, "Arnaud", StringComparison.OrdinalIgnoreCase)), user => Assert.False(user.IsAdmin));
    }

    [Fact]
    public async Task SeedAsync_ParisAdminFallbacksToLowestIdentifierWhenArnaudMissing()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var parisShopId = await SeedParisUsersAsync(scope.ServiceProvider, ParisUsersWithoutArnaud);

        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var parisUsers = (await connection.QueryAsync<ShopUserRow>(
                """
                SELECT "Id", "DisplayName", "IsAdmin", "Disabled"
                FROM "ShopUser"
                WHERE "ShopId" = @ShopId
                ORDER BY "Id";
                """,
                new { ShopId = parisShopId }))
            .ToList();

        Assert.Equal(ParisUsersWithoutArnaud.Count, parisUsers.Count);
        var admin = Assert.Single(parisUsers.Where(user => user.IsAdmin && !user.Disabled));
        Assert.Equal("Alice", admin.DisplayName);
    }

    [Fact]
    public async Task SeedAsync_NonParisShopsContainExpectedActiveUsers()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        await SeedParisUsersAsync(scope.ServiceProvider, ParisUsersWithArnaud);
        await SeedLegacyNonParisUserAsync(scope.ServiceProvider, "CinéBoutique Bordeaux", "legacy", "Ancien", true);

        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        foreach (var expected in ExpectedActiveUserCountByShop)
        {
            var users = (await connection.QueryAsync<ShopUserRowWithShop>(
                    """
                    SELECT su."Id", su."DisplayName", su."IsAdmin", su."Disabled", su."ShopId", s."Name" AS ShopName
                    FROM "ShopUser" su
                    JOIN "Shop" s ON s."Id" = su."ShopId"
                    WHERE s."Name" = @ShopName;
                    """,
                    new { ShopName = expected.Key }))
                .ToList();

            var activeUsers = users.Where(user => !user.Disabled).ToList();
            Assert.Equal(expected.Value, activeUsers.Count);
            Assert.Equal(1, activeUsers.Count(user => user.IsAdmin));

            if (!string.Equals(expected.Key, "CinéBoutique Paris", StringComparison.OrdinalIgnoreCase))
            {
                var displayNames = activeUsers.Select(user => user.DisplayName).OrderBy(name => name).ToList();
                Assert.Equal(NonParisExpectedDisplayNames, displayNames);
                var admin = Assert.Single(activeUsers.Where(user => user.IsAdmin));
                Assert.Equal("Administrateur", admin.DisplayName);

                var legacy = Assert.Single(users.Where(user => string.Equals(user.DisplayName, "Ancien", StringComparison.OrdinalIgnoreCase)));
                Assert.True(legacy.Disabled);
                Assert.False(legacy.IsAdmin);
            }
        }
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        await SeedParisUsersAsync(scope.ServiceProvider, ParisUsersWithArnaud);

        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        var snapshot = await SnapshotActiveUsersAsync(scope.ServiceProvider);

        await seeder.SeedAsync();

        var secondSnapshot = await SnapshotActiveUsersAsync(scope.ServiceProvider);

        Assert.Equal(snapshot.Count, secondSnapshot.Count);

        foreach (var (shopId, users) in snapshot)
        {
            Assert.True(secondSnapshot.TryGetValue(shopId, out var other));
            Assert.Equal(users.Count, other.Count);

            foreach (var user in users.OrderBy(user => user.Id))
            {
                var matching = Assert.Single(other.Where(candidate => candidate.Id == user.Id));
                Assert.Equal(user.DisplayName, matching.DisplayName);
                Assert.Equal(user.IsAdmin, matching.IsAdmin);
                Assert.Equal(user.Disabled, matching.Disabled);
            }
        }
    }

    private static async Task<IDictionary<Guid, List<ShopUserRow>>> SnapshotActiveUsersAsync(IServiceProvider services)
    {
        var connectionFactory = services.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var rows = await connection.QueryAsync<ShopUserRow>(
            """
            SELECT "Id", "DisplayName", "IsAdmin", "Disabled", "ShopId"
            FROM "ShopUser";
            """ );

        return rows.GroupBy(row => row.ShopId).ToDictionary(group => group.Key, group => group.OrderBy(user => user.Id).ToList());
    }

    private static async Task<Guid> SeedParisUsersAsync(IServiceProvider services, IReadOnlyList<ParisUserSeed> seeds)
    {
        var connectionFactory = services.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var shopId = await EnsureShopAsync(connection, "CinéBoutique Paris");

        foreach (var seed in seeds)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO "ShopUser" ("Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Secret_Hash", "Disabled")
                VALUES (@Id, @ShopId, @Login, @DisplayName, @IsAdmin, '', @Disabled)
                ON CONFLICT DO NOTHING;
                """,
                new
                {
                    seed.Id,
                    ShopId = shopId,
                    seed.Login,
                    seed.DisplayName,
                    seed.IsAdmin,
                    Disabled = seed.Disabled
                });
        }

        return shopId;
    }

    private static async Task SeedLegacyNonParisUserAsync(IServiceProvider services, string shopName, string login, string displayName, bool isAdmin)
    {
        var connectionFactory = services.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var shopId = await EnsureShopAsync(connection, shopName);

        await connection.ExecuteAsync(
            """
            INSERT INTO "ShopUser" ("Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Secret_Hash", "Disabled")
            VALUES (@Id, @ShopId, @Login, @DisplayName, @IsAdmin, '', FALSE)
            ON CONFLICT DO NOTHING;
            """,
            new
            {
                Id = Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
                ShopId = shopId,
                Login = login,
                DisplayName = displayName,
                IsAdmin = isAdmin
            });
    }

    private static async Task<Guid> EnsureShopAsync(NpgsqlConnection connection, string name)
    {
        var existingId = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name) LIMIT 1;",
            new { Name = name });

        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO \"Shop\" (\"Id\", \"Name\") VALUES (@Id, @Name);",
            new { Id = id, Name = name });
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
    private sealed record ParisUserSeed(Guid Id, string Login, string DisplayName, bool IsAdmin, bool Disabled);

    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated via Dapper materializer.")]
    private sealed record ShopUserRow(Guid Id, string DisplayName, bool IsAdmin, bool Disabled, Guid ShopId);

    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated via Dapper materializer.")]
    private sealed record ShopUserRowWithShop(Guid Id, string DisplayName, bool IsAdmin, bool Disabled, Guid ShopId, string ShopName);
}
