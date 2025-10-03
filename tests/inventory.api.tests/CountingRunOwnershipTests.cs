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
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class CountingRunOwnershipTests : IAsyncLifetime
{
    private readonly PostgresTestContainerFixture _pg;
    private InventoryApiApplicationFactory _factory = default!;

    public CountingRunOwnershipTests(PostgresTestContainerFixture pg)
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
    public async Task OwnerUserId_AllowsOptionalJoinWithShopUser()
    {
        await ResetDatabaseAsync();

        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        var hasOwnerColumn = await CountingRunSqlHelper.HasOwnerUserIdAsync(connection);
        Assert.True(hasOwnerColumn, "La colonne OwnerUserId est requise pour ce test.");

        var shopId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO \"Shop\" (\"Id\", \"Name\") VALUES (@Id, @Name);",
            new { Id = shopId, Name = $"Boutique test {Guid.NewGuid():N}" });

        var locationId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\", \"ShopId\") VALUES (@Id, @Code, @Label, @ShopId);",
            new
            {
                Id = locationId,
                Code = "Z1",
                Label = "Zone Z1",
                ShopId = shopId
            });

        var sessionWithOwnerId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc);",
            new
            {
                Id = sessionWithOwnerId,
                Name = "Session propriétaire",
                StartedAtUtc = DateTimeOffset.UtcNow
            });

        var sessionWithoutOwnerId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc);",
            new
            {
                Id = sessionWithoutOwnerId,
                Name = "Session libre",
                StartedAtUtc = DateTimeOffset.UtcNow
            });

        var ownerUserId = await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO "ShopUser" ("ShopId", "Login", "DisplayName", "IsAdmin", "Secret_Hash", "Disabled")
            VALUES (@ShopId, @Login, @DisplayName, FALSE, '', FALSE)
            RETURNING "Id";
            """,
            new
            {
                ShopId = shopId,
                Login = "compteur1",
                DisplayName = "Camille Compteur"
            });

        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var completedAt = startedAt.AddMinutes(2);

        var runWithOwnerId = Guid.NewGuid();
        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                runWithOwnerId,
                sessionWithOwnerId,
                locationId,
                CountType: 1,
                StartedAtUtc: startedAt,
                CompletedAtUtc: completedAt,
                OperatorDisplayName: "Camille Compteur",
                OwnerUserId: ownerUserId),
            CancellationToken.None);

        var runWithoutOwnerId = Guid.NewGuid();
        await CountingRunSqlHelper.InsertAsync(
            connection,
            new CountingRunInsert(
                runWithoutOwnerId,
                sessionWithoutOwnerId,
                locationId,
                CountType: 2,
                StartedAtUtc: startedAt,
                CompletedAtUtc: completedAt,
                OperatorDisplayName: "Équipe nuit"),
            CancellationToken.None);

        const string joinSql =
            """
            SELECT cr."Id" AS RunId, su."DisplayName" AS OwnerDisplayName
            FROM "CountingRun" cr
            LEFT JOIN "ShopUser" su ON su."Id" = cr."OwnerUserId"
            WHERE cr."Id" = ANY(@RunIds)
            ORDER BY cr."Id";
            """;

        var rows = (await connection.QueryAsync<(Guid RunId, string? OwnerDisplayName)>(
                joinSql,
                new { RunIds = new[] { runWithOwnerId, runWithoutOwnerId } }))
            .ToList();

        Assert.Equal(2, rows.Count);

        var ownedRun = Assert.Single(rows.Where(row => row.RunId == runWithOwnerId));
        Assert.Equal("Camille Compteur", ownedRun.OwnerDisplayName);

        var unownedRun = Assert.Single(rows.Where(row => row.RunId == runWithoutOwnerId));
        Assert.Null(unownedRun.OwnerDisplayName);
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connectionFactory.CreateOpenConnectionAsync(CancellationToken.None);

        const string cleanupSql =
            """
            DO $do$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'Audit') THEN
                    EXECUTE 'TRUNCATE TABLE "Audit" RESTART IDENTITY CASCADE;';
                END IF;
            END $do$;

            TRUNCATE TABLE "Conflict" RESTART IDENTITY CASCADE;
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
}
