#pragma warning disable CA1001
#pragma warning disable CA1707
#pragma warning disable CA2007
#pragma warning disable CA2234

using System;
using System.Collections.Generic;
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
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;

    private static readonly IReadOnlyDictionary<string, int> ExpectedUserCountByShop = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["CinéBoutique Paris"] = 6,
        ["CinéBoutique Bordeaux"] = 5,
        ["CinéBoutique Montpellier"] = 5,
        ["CinéBoutique Marseille"] = 5,
        ["CinéBoutique Bruxelles"] = 5
    };

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
    public async Task SeedAsync_CreatesAdministratorPerShop()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var shopUsers = (await connection.QueryAsync<ShopUserRow>(
                """
                SELECT su."Login", su."DisplayName", su."IsAdmin", su."Disabled", su."ShopId", s."Name" AS ShopName
                FROM "ShopUser" su
                JOIN "Shop" s ON s."Id" = su."ShopId";
                """))
            .ToList();

        foreach (var expectedShop in ExpectedUserCountByShop.Keys)
        {
            var admin = shopUsers.SingleOrDefault(
                user => string.Equals(user.ShopName, expectedShop, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(user.Login, "administrateur", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(admin);
            Assert.True(admin!.IsAdmin);
            Assert.False(admin.Disabled);
            Assert.Equal("Administrateur", admin.DisplayName);
        }
    }

    [Fact]
    public async Task SeedAsync_ProducesExpectedAccountCountsPerShop()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<InventoryDataSeeder>();
        await seeder.SeedAsync();

        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var counts = await connection.QueryAsync<(string ShopName, int UserCount)>(
            """
            SELECT s."Name" AS ShopName, COUNT(*) AS UserCount
            FROM "ShopUser" su
            JOIN "Shop" s ON s."Id" = su."ShopId"
            GROUP BY s."Name";
            """
        );

        var actual = counts.ToDictionary(row => row.ShopName, row => row.UserCount, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(ExpectedUserCountByShop.Count, actual.Count);

        foreach (var expected in ExpectedUserCountByShop)
        {
            Assert.True(actual.TryGetValue(expected.Key, out var count));
            Assert.Equal(expected.Value, count);
        }
    }

    [Fact]
    public async Task DuplicateLoginPerShop_IsRejectedRegardlessOfCase()
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

        var exception = await Assert.ThrowsAsync<PostgresException>(
            async () =>
                await connection.ExecuteAsync(
                    "INSERT INTO \"ShopUser\" (\"ShopId\", \"Login\", \"DisplayName\", \"IsAdmin\", \"Secret_Hash\", \"Disabled\") VALUES (@ShopId, @Login, 'Duplicate', false, '', false);",
                    new { ShopId = parisShopId, Login = "ADMINISTRATEUR" }));

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
TRUNCATE TABLE "ShopUser" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Shop" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "Product" RESTART IDENTITY CASCADE;
TRUNCATE TABLE "audit_logs" RESTART IDENTITY CASCADE;
""";

        await connection.ExecuteAsync(cleanupSql);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instancié via la réflexion de Dapper lors du mapping des résultats.")]
    private sealed record ShopUserRow(string Login, string DisplayName, bool IsAdmin, bool Disabled, Guid ShopId, string ShopName);
}
