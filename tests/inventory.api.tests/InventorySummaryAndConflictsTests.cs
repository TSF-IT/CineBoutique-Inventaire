using System.Net;
using System.Net.Http.Json;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;

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
        client.SetBearerToken(JwtTestTokenFactory.CreateOperatorToken());

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
        client.SetBearerToken(JwtTestTokenFactory.CreateOperatorToken());

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
    public async Task Locations_ListByShop_ReturnsZonesWithStates()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedInventoryContextAsync().ConfigureAwait(false);
        var client = CreateClient();
        client.SetBearerToken(JwtTestTokenFactory.CreateOperatorToken());

        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, seeded.PrimaryUserId, 1)
        ).ConfigureAwait(false);
        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run to mark busy");
        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var locationsResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/locations?shopId={seeded.ShopId}")
        ).ConfigureAwait(false);

        await locationsResponse.ShouldBeAsync(HttpStatusCode.OK, "list locations").ConfigureAwait(false);
        var locations = await locationsResponse.Content.ReadFromJsonAsync<LocationListItemDto[]>().ConfigureAwait(false);
        locations.Should().NotBeNull();
        locations!.Should().ContainSingle(loc => loc.Id == seeded.LocationId);
        var location = locations!.Single(loc => loc.Id == seeded.LocationId);
        location.IsBusy.Should().BeTrue();
        location.ActiveRunId.Should().Be(started!.RunId);
        location.CountStatuses.Should().NotBeNullOrEmpty();
        location.CountStatuses.Any(status => status.CountType == 1 && status.Status == LocationCountStatus.InProgress)
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
            await seeder.CreateProductAsync(productSku, "Produit Conflit", productEan).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return (shopId, locationId, primaryUserId, secondaryUserId, productEan);
    }

    private static async Task<CompleteInventoryRunResponse?> RunCountAsync(
        HttpClient client,
        (Guid ShopId, Guid LocationId, Guid PrimaryUserId, Guid SecondaryUserId, string ProductEan) seeded,
        Guid operatorId,
        short countType,
        decimal quantity)
    {
        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            new StartRunRequest(seeded.ShopId, operatorId, countType)
        ).ConfigureAwait(false);
        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run for count").ConfigureAwait(false);
        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var completeResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/complete"),
            new CompleteRunRequest(
                started!.RunId,
                operatorId,
                countType,
                [new CompleteRunItemRequest(seeded.ProductEan, quantity, false)]
            )
        ).ConfigureAwait(false);
        await completeResponse.ShouldBeAsync(HttpStatusCode.OK, "complete run for count");
        return await completeResponse.Content.ReadFromJsonAsync<CompleteInventoryRunResponse>().ConfigureAwait(false);
    }
}
