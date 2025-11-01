using System.Linq;
using System.Net;
using System.Net.Http.Json;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Npgsql;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class InventorySummaryAndConflictsTests : IntegrationTestBase
{
    public InventorySummaryAndConflictsTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task Summary_WhenCountsDisagree_ShowsConflictForLocation()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        await RunCountAsync(client, seeded, seeded.PrimaryUserId, 1, 5m).ConfigureAwait(false);
        await RunCountAsync(client, seeded, seeded.SecondaryUserId, 2, 3m).ConfigureAwait(false);

        var summaryResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/summary?shopId={seeded.ShopId}")
        ).ConfigureAwait(false);

        await summaryResponse.ShouldBeAsync(HttpStatusCode.OK, "inventory summary").ConfigureAwait(false);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        summary.Should().NotBeNull();
        summary!.Conflicts.Should().BeGreaterOrEqualTo(1);
        summary.ConflictZones.Should().NotBeNull();
        summary.ConflictZones.Should().Contain(zone => zone.LocationId == seeded.LocationId);
    }

    [SkippableFact]
    public async Task ConflictsEndpoint_ListsConflicts_ThenResolvingClearsIt()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var firstRun = await RunCountAsync(client, seeded, seeded.PrimaryUserId, 1, 6m).ConfigureAwait(false);
        firstRun.Should().NotBeNull();
        var secondRun = await RunCountAsync(client, seeded, seeded.SecondaryUserId, 2, 2m).ConfigureAwait(false);
        secondRun.Should().NotBeNull();

        var conflictResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/conflicts/{seeded.LocationId}")
        ).ConfigureAwait(false);
        await conflictResponse.ShouldBeAsync(HttpStatusCode.OK, "conflicts before resolution").ConfigureAwait(false);
        var conflict = await conflictResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        conflict.Should().NotBeNull();
        conflict!.Items.Should().NotBeNull();
        conflict.Items.Should().NotBeEmpty();
        conflict.Items.SelectMany(item => item.AllCounts).Should().NotBeEmpty();

        await RunCountAsync(client, seeded, seeded.PrimaryUserId, 1, 6m).ConfigureAwait(false);
        await RunCountAsync(client, seeded, seeded.SecondaryUserId, 2, 6m).ConfigureAwait(false);

        var resolvedResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/conflicts/{seeded.LocationId}")
        ).ConfigureAwait(false);
        await resolvedResponse.ShouldBeAsync(HttpStatusCode.OK, "conflicts after resolution").ConfigureAwait(false);
        var resolved = await resolvedResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        resolved.Should().NotBeNull();
        resolved!.Items.Should().BeEmpty("conflit résolu");
    }

    [SkippableFact]
    public async Task ConflictsEndpoint_FlagsMissingProductWhenSecondRunOmitsItem()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextWithAdditionalProductAsync().ConfigureAwait(false);
        var client = CreateClient();

        var firstRunItems = new[]
        {
            new CompleteRunItemRequest(seeded.ConflictEan, 2m, false),
            new CompleteRunItemRequest(seeded.StableEan, 6m, false)
        };
        var firstRun = await RunCountAsync(
            client,
            seeded.ShopId,
            seeded.LocationId,
            seeded.PrimaryUserId,
            1,
            firstRunItems
        ).ConfigureAwait(false);
        firstRun.Should().NotBeNull();

        var secondRunItems = new[]
        {
            new CompleteRunItemRequest(seeded.StableEan, 4m, false)
        };
        var secondRun = await RunCountAsync(
            client,
            seeded.ShopId,
            seeded.LocationId,
            seeded.SecondaryUserId,
            2,
            secondRunItems
        ).ConfigureAwait(false);
        secondRun.Should().NotBeNull();

        var conflictResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/conflicts/{seeded.LocationId}")
        ).ConfigureAwait(false);

        await conflictResponse.ShouldBeAsync(HttpStatusCode.OK, "conflict detail should list omissions").ConfigureAwait(false);
        var conflict = await conflictResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        conflict.Should().NotBeNull();
        conflict!.Items.Should().HaveCount(2, "les deux références comptées au premier passage doivent apparaître en conflit");

        var missingProduct = conflict.Items.Single(item => item.Ean == seeded.ConflictEan);
        missingProduct.QtyC1.Should().Be(2, "le premier comptage a trouvé 2 unités");
        missingProduct.QtyC2.Should().Be(0, "le second comptage a omis la référence");

        var sharedProduct = conflict.Items.Single(item => item.Ean == seeded.StableEan);
        sharedProduct.QtyC1.Should().Be(6);
        sharedProduct.QtyC2.Should().Be(4);
    }

    [SkippableFact]
    public async Task ConflictsEndpoint_IncludesBaselineCountsForActiveSession()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var firstRun = await RunCountAsync(client, seeded, seeded.PrimaryUserId, 1, 10m).ConfigureAwait(false);
        firstRun.Should().NotBeNull();
        var secondRun = await RunCountAsync(client, seeded, seeded.SecondaryUserId, 2, 9m).ConfigureAwait(false);
        secondRun.Should().NotBeNull();

        var conflictResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/conflicts/{seeded.LocationId}")
        ).ConfigureAwait(false);

        await conflictResponse.ShouldBeAsync(HttpStatusCode.OK, "conflict detail with successive counts").ConfigureAwait(false);
        var conflict = await conflictResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        conflict.Should().NotBeNull();
        conflict!.Runs.Should().NotBeNull();
        conflict.Runs.Should().Contain(run => run.CountType == 1);
        conflict.Runs.Should().Contain(run => run.CountType == 2);

        var item = conflict.Items.Should().ContainSingle().Subject;
        item.QtyC1.Should().Be(10);
        item.QtyC2.Should().Be(9);
        item.Delta.Should().Be(1);
    }

    [SkippableFact]
    public async Task Conflicts_AreCleared_WhenThirdCountMatchesAnExistingRun()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        await RunCountAsync(client, seeded, seeded.PrimaryUserId, 1, 10m).ConfigureAwait(false);
        await RunCountAsync(client, seeded, seeded.SecondaryUserId, 2, 9m).ConfigureAwait(false);

        var initialConflictResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/conflicts/{seeded.LocationId}")
        ).ConfigureAwait(false);

        await initialConflictResponse.ShouldBeAsync(HttpStatusCode.OK, "conflict detail after two runs").ConfigureAwait(false);
        var initialConflict = await initialConflictResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        initialConflict.Should().NotBeNull();
        initialConflict!.Items.Should().NotBeEmpty("un écart doit être détecté après deux comptages divergents");

        await RunCountAsync(client, seeded, seeded.PrimaryUserId, 3, 10m).ConfigureAwait(false);

        var resolvedConflictResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/conflicts/{seeded.LocationId}")
        ).ConfigureAwait(false);

        await resolvedConflictResponse.ShouldBeAsync(HttpStatusCode.OK, "conflict detail after third run").ConfigureAwait(false);
        var resolvedConflict = await resolvedConflictResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        resolvedConflict.Should().NotBeNull();
        resolvedConflict!.Items.Should().BeEmpty("le troisième comptage correspond au premier");

        var summaryResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/summary?shopId={seeded.ShopId}")
        ).ConfigureAwait(false);

        await summaryResponse.ShouldBeAsync(HttpStatusCode.OK, "inventory summary after resolution").ConfigureAwait(false);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        summary.Should().NotBeNull();
        summary!.ConflictZones.Should().NotContain(zone => zone.LocationId == seeded.LocationId, "la zone devrait être résolue");
    }

    [SkippableFact]
    public async Task ConflictDetail_IncludesAllRuns_WhenConflictsPersistAcrossCounts()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        await RunCountAsync(client, seeded, seeded.PrimaryUserId, 1, 10m).ConfigureAwait(false);
        await RunCountAsync(client, seeded, seeded.SecondaryUserId, 2, 9m).ConfigureAwait(false);
        await RunCountAsync(client, seeded, seeded.PrimaryUserId, 3, 12m).ConfigureAwait(false);

        var conflictResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/conflicts/{seeded.LocationId}")
        ).ConfigureAwait(false);

        await conflictResponse.ShouldBeAsync(HttpStatusCode.OK, "conflict detail after three divergent runs").ConfigureAwait(false);
        var conflict = await conflictResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        conflict.Should().NotBeNull();
        conflict!.Runs.Should().Contain(run => run.CountType == 1);
        conflict.Runs.Should().Contain(run => run.CountType == 2);
        conflict.Runs.Should().Contain(run => run.CountType == 3);

        var item = conflict.Items.Should().ContainSingle().Subject;
        item.AllCounts.Should().NotBeNull();
        item.AllCounts.Should().Contain(count => count.CountType == 3 && count.Quantity == 12);
    }

    [SkippableFact]
    public async Task Conflicts_AreCleared_WhenThirdCountFocusesOnConflictingItems()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextWithAdditionalProductAsync().ConfigureAwait(false);
        var client = CreateClient();

        await RunCountAsync(
            client,
            seeded.ShopId,
            seeded.LocationId,
            seeded.PrimaryUserId,
            1,
            new[]
            {
                new CompleteRunItemRequest(seeded.ConflictEan, 10m, false),
                new CompleteRunItemRequest(seeded.StableEan, 10m, false),
            }).ConfigureAwait(false);

        await RunCountAsync(
            client,
            seeded.ShopId,
            seeded.LocationId,
            seeded.SecondaryUserId,
            2,
            new[]
            {
                new CompleteRunItemRequest(seeded.ConflictEan, 9m, false),
                new CompleteRunItemRequest(seeded.StableEan, 10m, false),
            }).ConfigureAwait(false);

        var conflictResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/conflicts/{seeded.LocationId}")
        ).ConfigureAwait(false);

        await conflictResponse.ShouldBeAsync(HttpStatusCode.OK, "conflict detail after two populated runs").ConfigureAwait(false);
        var conflict = await conflictResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        conflict.Should().NotBeNull();
        conflict!.Items.Should().NotBeEmpty();

        await RunCountAsync(
            client,
            seeded.ShopId,
            seeded.LocationId,
            seeded.PrimaryUserId,
            3,
            new[]
            {
                new CompleteRunItemRequest(seeded.ConflictEan, 10m, false),
            }).ConfigureAwait(false);

        var resolvedConflictResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/conflicts/{seeded.LocationId}")
        ).ConfigureAwait(false);

        await resolvedConflictResponse.ShouldBeAsync(HttpStatusCode.OK, "conflict detail after focused third run").ConfigureAwait(false);
        var resolvedConflict = await resolvedConflictResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        resolvedConflict.Should().NotBeNull();
        resolvedConflict!.Items.Should().BeEmpty("le troisième comptage a confirmé la valeur du premier comptage");

        var summaryResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/summary?shopId={seeded.ShopId}")
        ).ConfigureAwait(false);

        await summaryResponse.ShouldBeAsync(HttpStatusCode.OK, "inventory summary after focused third run").ConfigureAwait(false);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        summary.Should().NotBeNull();
        summary!.ConflictZones.Should().NotContain(zone => zone.LocationId == seeded.LocationId);
    }

    [SkippableFact]
    public async Task Locations_ListByShop_ReturnsZonesWithStates()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();

        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 1)
        ).ConfigureAwait(false);
        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run").ConfigureAwait(false);
        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var initialLocations = await client.GetAsync(
            client.CreateRelativeUri($"/api/locations?shopId={seeded.ShopId}")
        ).ConfigureAwait(false);

        await initialLocations.ShouldBeAsync(HttpStatusCode.OK, "list locations right after start").ConfigureAwait(false);
        var locations = await initialLocations.Content.ReadFromJsonAsync<LocationListItemDto[]>().ConfigureAwait(false);
        locations.Should().NotBeNull();
        locations!.Should().ContainSingle(loc => loc.Id == seeded.LocationId);
        var location = locations.Single(loc => loc.Id == seeded.LocationId);
        location.IsBusy.Should().BeTrue("un comptage vient de démarrer");
        location.ActiveRunId.Should().Be(started!.RunId);
        location.CountStatuses.Should().NotBeNull();
        location.CountStatuses.Any(status => status.CountType == 1 && status.Status == LocationCountStatus.InProgress)
            .Should().BeTrue();

        await InsertCountLineAsync(started.RunId, seeded.ProductEan, 1m).ConfigureAwait(false);

        var updatedResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/locations?shopId={seeded.ShopId}")
        ).ConfigureAwait(false);

        await updatedResponse.ShouldBeAsync(HttpStatusCode.OK, "list locations after first line").ConfigureAwait(false);
        var updatedLocations = await updatedResponse.Content.ReadFromJsonAsync<LocationListItemDto[]>().ConfigureAwait(false);
        updatedLocations.Should().NotBeNull();
        updatedLocations!.Should().ContainSingle(loc => loc.Id == seeded.LocationId);
        var updated = updatedLocations.Single(loc => loc.Id == seeded.LocationId);
        updated.IsBusy.Should().BeTrue();
        updated.ActiveRunId.Should().Be(started.RunId);
        updated.CountStatuses.Should().NotBeNullOrEmpty();
        updated.CountStatuses.Any(status => status.CountType == 1 && status.Status == LocationCountStatus.InProgress)
            .Should().BeTrue();
    }

    private async Task<(Guid ShopId, Guid LocationId, Guid PrimaryUserId, Guid SecondaryUserId, string ProductEan)> SeedInventoryContextAsync()
    {
        var shopId = Guid.Empty;
        var locationId = Guid.Empty;
        var primaryUserId = Guid.Empty;
        var secondaryUserId = Guid.Empty;
        const string productSku = "SKU-CONFLICT-001";
        const string productEan = "2345678901234";

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Conflit").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "LOC-CONF", "Zone Conflits").ConfigureAwait(false);
            primaryUserId = await seeder.CreateShopUserAsync(shopId, "charlie", "Charlie").ConfigureAwait(false);
            secondaryUserId = await seeder.CreateShopUserAsync(shopId, "diana", "Diana").ConfigureAwait(false);
            await seeder.CreateProductAsync(shopId, productSku, "Produit Conflit", productEan).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return (shopId, locationId, primaryUserId, secondaryUserId, productEan);
    }

    private async Task<(Guid ShopId, Guid LocationId, Guid PrimaryUserId, Guid SecondaryUserId, string ConflictEan, string StableEan)> SeedInventoryContextWithAdditionalProductAsync()
    {
        var shopId = Guid.Empty;
        var locationId = Guid.Empty;
        var primaryUserId = Guid.Empty;
        var secondaryUserId = Guid.Empty;

        const string conflictSku = "SKU-CONFLICT-001";
        const string conflictEan = "2345678901234";
        const string stableSku = "SKU-STABLE-001";
        const string stableEan = "3345678901234";

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Conflit").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "LOC-CONF", "Zone Conflits").ConfigureAwait(false);
            primaryUserId = await seeder.CreateShopUserAsync(shopId, "charlie", "Charlie").ConfigureAwait(false);
            secondaryUserId = await seeder.CreateShopUserAsync(shopId, "diana", "Diana").ConfigureAwait(false);
            await seeder.CreateProductAsync(shopId, conflictSku, "Produit Conflit", conflictEan).ConfigureAwait(false);
            await seeder.CreateProductAsync(shopId, stableSku, "Produit Stable", stableEan).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return (shopId, locationId, primaryUserId, secondaryUserId, conflictEan, stableEan);
    }

    private static async Task<CompleteInventoryRunResponse?> RunCountAsync(
        HttpClient client,
        (Guid ShopId, Guid LocationId, Guid PrimaryUserId, Guid SecondaryUserId, string ProductEan) seeded,
        Guid operatorId,
        short countType,
        decimal quantity)
    {
        var items = new[]
        {
            new CompleteRunItemRequest(seeded.ProductEan, quantity, false)
        };

        return await RunCountAsync(
            client,
            seeded.ShopId,
            seeded.LocationId,
            operatorId,
            countType,
            items).ConfigureAwait(false);
    }

    private static async Task<CompleteInventoryRunResponse?> RunCountAsync(
        HttpClient client,
        Guid shopId,
        Guid locationId,
        Guid operatorId,
        short countType,
        IReadOnlyList<CompleteRunItemRequest> items)
    {
        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, operatorId, countType)
        ).ConfigureAwait(false);
        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run for count").ConfigureAwait(false);
        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var completeResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
            new CompleteRunRequest(
                started!.RunId,
                operatorId,
                countType,
                items.ToArray()
            )
        ).ConfigureAwait(false);
        await completeResponse.ShouldBeAsync(HttpStatusCode.OK, "complete run for count");
        return await completeResponse.Content.ReadFromJsonAsync<CompleteInventoryRunResponse>().ConfigureAwait(false);
    }

    private async Task InsertCountLineAsync(Guid runId, string ean, decimal quantity)
    {
        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        Guid productId;
        await using (var lookup = new NpgsqlCommand("SELECT \"Id\" FROM \"Product\" WHERE \"Ean\" = @ean LIMIT 1;", connection, transaction))
        {
            lookup.Parameters.AddWithValue("ean", ean);
            var result = await lookup.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is not Guid id)
            {
                throw new InvalidOperationException($"Produit introuvable pour l'EAN {ean}.");
            }

            productId = id;
        }

        await using (var insert = new NpgsqlCommand(
                   "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@id, @runId, @productId, @quantity, @at);",
                   connection,
                   transaction))
        {
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("runId", runId);
            insert.Parameters.AddWithValue("productId", productId);
            insert.Parameters.AddWithValue("quantity", quantity);
            insert.Parameters.AddWithValue("at", DateTimeOffset.UtcNow);
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }
}
