using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class InventoryResetEndpointsTests : IntegrationTestBase
{
    public InventoryResetEndpointsTests(InventoryApiFixture fx) => UseFixture(fx);

    [SkippableFact]
    public async Task ResetShopInventory_RemovesAllRunsLinesAndConflicts()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var context = await SeedSingleShopAsync().ConfigureAwait(false);
        var client = CreateClient();

        var runId = await StartRunAsync(client, context.LocationId, context.ShopId, context.UserId).ConfigureAwait(false);
        var completeResponse = await CompleteRunAsync(
                client,
                context.LocationId,
                runId,
                context.UserId,
                1,
                (context.ProductEan, 3m))
            .ConfigureAwait(false);
        await completeResponse.ShouldBeAsync(HttpStatusCode.OK, "complete run to create count lines").ConfigureAwait(false);

        await InsertConflictForRunAsync(runId).ConfigureAwait(false);

        var resetResponse = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Post,
            client.CreateRelativeUri($"/api/shops/{context.ShopId}/inventories/reset"))).ConfigureAwait(false);
        await resetResponse.ShouldBeAsync(HttpStatusCode.OK, "reset must purge the inventory state").ConfigureAwait(false);

        var payload = await resetResponse.Content.ReadFromJsonAsync<ResetShopInventoryResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.RunsCleared.Should().BeGreaterOrEqualTo(1);
        payload.LinesCleared.Should().BeGreaterOrEqualTo(1);
        payload.ConflictsCleared.Should().BeGreaterOrEqualTo(1);
        payload.ZonesCleared.Should().BeGreaterOrEqualTo(1);
        payload.SessionsClosed.Should().BeGreaterOrEqualTo(1);

        await AssertInventoryTablesEmptyForShop(context.ShopId).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task ResetShopInventory_RemovesDanglingLinesLinkedOnlyThroughProducts()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var (target, foreignShop) = await SeedTwoShopsAsync().ConfigureAwait(false);
        var client = CreateClient();

        var foreignRunId = await StartRunAsync(client, foreignShop.LocationId, foreignShop.ShopId, foreignShop.UserId).ConfigureAwait(false);
        await InsertDanglingLineAndConflictAsync(foreignRunId, target.ProductId).ConfigureAwait(false);

        var resetResponse = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Post,
            client.CreateRelativeUri($"/api/shops/{target.ShopId}/inventories/reset"))).ConfigureAwait(false);
        await resetResponse.ShouldBeAsync(HttpStatusCode.OK, "reset must clear product-linked lines even without runs").ConfigureAwait(false);

        var payload = await resetResponse.Content.ReadFromJsonAsync<ResetShopInventoryResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.RunsCleared.Should().Be(0);
        payload.ZonesCleared.Should().Be(0);
        payload.LinesCleared.Should().BeGreaterOrEqualTo(1);
        payload.ConflictsCleared.Should().BeGreaterOrEqualTo(1);

        await AssertInventoryTablesEmptyForShop(target.ShopId).ConfigureAwait(false);
        await AssertRunStillExistsAsync(foreignRunId).ConfigureAwait(false);
    }

    private async Task<ShopInventoryContext> SeedSingleShopAsync()
    {
        ShopInventoryContext context = default!;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            context = await CreateShopContextAsync(
                    seeder,
                    "Reset Boutique",
                    "RST-A1",
                    "RST-SKU-A1",
                    "5900000000011")
                .ConfigureAwait(false);
        }).ConfigureAwait(false);

        return context;
    }

    private async Task<(ShopInventoryContext Target, ShopInventoryContext Foreign)> SeedTwoShopsAsync()
    {
        ShopInventoryContext target = default!;
        ShopInventoryContext foreign = default!;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            target = await CreateShopContextAsync(
                    seeder,
                    "Reset Boutique",
                    "RST-TGT",
                    "RST-SKU-TGT",
                    "5900000000028")
                .ConfigureAwait(false);

            foreign = await CreateShopContextAsync(
                    seeder,
                    "Autre Boutique",
                    "RST-FRG",
                    "RST-SKU-FRG",
                    "5900000000035")
                .ConfigureAwait(false);
        }).ConfigureAwait(false);

        return (target, foreign);
    }

    private static async Task<ShopInventoryContext> CreateShopContextAsync(
        TestDataSeeder seeder,
        string shopName,
        string locationCode,
        string sku,
        string ean)
    {
        var shopId = await seeder.CreateShopAsync(shopName).ConfigureAwait(false);
        var locationId = await seeder.CreateLocationAsync(shopId, locationCode, $"Zone {locationCode}").ConfigureAwait(false);
        var userLogin = $"{locationCode.ToLowerInvariant()}-operator";
        var userDisplayName = $"{locationCode} Operator";
        var userId = await seeder.CreateShopUserAsync(shopId, userLogin, userDisplayName, isAdmin: true).ConfigureAwait(false);
        var productId = await seeder.CreateProductAsync(shopId, sku, $"Produit {sku}", ean).ConfigureAwait(false);

        return new ShopInventoryContext(shopId, locationId, userId, productId, ean);
    }

    private async Task<Guid> StartRunAsync(HttpClient client, Guid locationId, Guid shopId, Guid ownerUserId)
    {
        var startResponse = await client.PostAsJsonAsync(
                client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
                new StartRunRequest(shopId, ownerUserId, 1))
            .ConfigureAwait(false);

        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run for reset scenario").ConfigureAwait(false);

        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();
        return started!.RunId;
    }

    private static Task<HttpResponseMessage> CompleteRunAsync(
        HttpClient client,
        Guid locationId,
        Guid runId,
        Guid ownerUserId,
        short countType,
        params (string ean, decimal quantity)[] items)
    {
        var payload = new CompleteRunRequest(
            runId,
            ownerUserId,
            countType,
            items.Select(item => new CompleteRunItemRequest(item.ean, item.quantity, false)).ToArray());

        return client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
            payload);
    }

    private async Task InsertConflictForRunAsync(Guid runId)
    {
        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        var countLineId = await GetFirstCountLineIdAsync(connection, transaction, runId).ConfigureAwait(false);

        const string insertSql = """
INSERT INTO "Conflict" ("Id", "CountLineId", "Status", "Notes", "CreatedAtUtc", "ResolvedAtUtc", "ResolvedQuantity", "IsResolved")
VALUES (@id, @countLineId, 'open', NULL, @createdAt, NULL, NULL, FALSE);
""";

        await using var insert = new NpgsqlCommand(insertSql, connection, transaction)
        {
            Parameters =
            {
                new("id", Guid.NewGuid()),
                new("countLineId", countLineId),
                new("createdAt", DateTimeOffset.UtcNow)
            }
        };

        await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
    }

    private async Task InsertDanglingLineAndConflictAsync(Guid runId, Guid productId)
    {
        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        var countLineId = Guid.NewGuid();
        const string insertLineSql = """
INSERT INTO "CountLine" ("Id", "CountingRunId", "ProductId", "Quantity", "CountedAtUtc")
VALUES (@id, @runId, @productId, @quantity, @countedAt);
""";

        await using (var insertLine = new NpgsqlCommand(insertLineSql, connection, transaction)
        {
            Parameters =
            {
                new("id", countLineId),
                new("runId", runId),
                new("productId", productId),
                new("quantity", 1m),
                new("countedAt", DateTimeOffset.UtcNow)
            }
        })
        {
            await insertLine.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        const string insertConflictSql = """
INSERT INTO "Conflict" ("Id", "CountLineId", "Status", "Notes", "CreatedAtUtc", "ResolvedAtUtc", "ResolvedQuantity", "IsResolved")
VALUES (@id, @countLineId, 'open', NULL, @createdAt, NULL, NULL, FALSE);
""";

        await using (var insertConflict = new NpgsqlCommand(insertConflictSql, connection, transaction)
        {
            Parameters =
            {
                new("id", Guid.NewGuid()),
                new("countLineId", countLineId),
                new("createdAt", DateTimeOffset.UtcNow)
            }
        })
        {
            await insertConflict.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    private static async Task<Guid> GetFirstCountLineIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId)
    {
        const string selectSql = """
SELECT "Id"
FROM "CountLine"
WHERE "CountingRunId" = @runId
LIMIT 1;
""";

        await using var select = new NpgsqlCommand(selectSql, connection, transaction)
        {
            Parameters = { new("runId", runId) }
        };

        var result = await select.ExecuteScalarAsync().ConfigureAwait(false);
        if (result is not Guid lineId)
        {
            throw new InvalidOperationException($"Aucune CountLine trouvée pour le run {runId}.");
        }

        return lineId;
    }

    private async Task AssertInventoryTablesEmptyForShop(Guid shopId)
    {
        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);

        static async Task<long> ReadCountAsync(NpgsqlConnection connection, string sql, Guid shopId)
        {
            await using var command = new NpgsqlCommand(sql, connection)
            {
                Parameters = { new("shopId", shopId) }
            };

            var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return Convert.ToInt64(result);
        }

        const string linesByRunSql = """
SELECT COUNT(1)
FROM "CountLine" cl
JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
JOIN "Location" l ON l."Id" = cr."LocationId"
WHERE l."ShopId" = @shopId;
""";
        const string linesByProductSql = """
SELECT COUNT(1)
FROM "CountLine" cl
JOIN "Product" p ON p."Id" = cl."ProductId"
WHERE p."ShopId" = @shopId;
""";
        const string conflictsByProductSql = """
SELECT COUNT(1)
FROM "Conflict" c
JOIN "CountLine" cl ON cl."Id" = c."CountLineId"
JOIN "Product" p ON p."Id" = cl."ProductId"
WHERE p."ShopId" = @shopId;
""";
        const string runsSql = """
SELECT COUNT(1)
FROM "CountingRun" cr
JOIN "Location" l ON l."Id" = cr."LocationId"
WHERE l."ShopId" = @shopId;
""";

        var linesByRun = await ReadCountAsync(connection, linesByRunSql, shopId).ConfigureAwait(false);
        var linesByProduct = await ReadCountAsync(connection, linesByProductSql, shopId).ConfigureAwait(false);
        var conflicts = await ReadCountAsync(connection, conflictsByProductSql, shopId).ConfigureAwait(false);
        var runs = await ReadCountAsync(connection, runsSql, shopId).ConfigureAwait(false);

        linesByRun.Should().Be(0, "no count line should remain for the shop through locations");
        linesByProduct.Should().Be(0, "no count line should remain for the shop through products");
        conflicts.Should().Be(0, "no conflict should remain for the shop");
        runs.Should().Be(0, "no run should remain for the shop");
    }

    private async Task AssertRunStillExistsAsync(Guid runId)
    {
        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        const string sql = "SELECT COUNT(1) FROM \"CountingRun\" WHERE \"Id\" = @runId;";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            Parameters = { new("runId", runId) }
        };

        var remaining = await command.ExecuteScalarAsync().ConfigureAwait(false);
        Convert.ToInt64(remaining).Should().Be(1, "reset must not delete runs from other shops");
    }

    private sealed record ShopInventoryContext(
        Guid ShopId,
        Guid LocationId,
        Guid UserId,
        Guid ProductId,
        string ProductEan);
}
