using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class InventoryCountingFlowTests : IntegrationTestBase
{
    public InventoryCountingFlowTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task ConflictsAreDetectedThenResolvedWhenCountsMatch()
    {
        SkipIfDockerUnavailable();

        Guid shopId = Guid.Empty;
        Guid locationId = Guid.Empty;
        Guid primaryUserId = Guid.Empty;
        Guid secondaryUserId = Guid.Empty;
        const string productSku = "SKU-001";
        const string productEan = "12345678";

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Tests").ConfigureAwait(true);
            locationId = await seeder.CreateLocationAsync(shopId, "Z-001", "Zone Pilote").ConfigureAwait(true);
            primaryUserId = await seeder.CreateShopUserAsync(shopId, "alice", "Alice").ConfigureAwait(true);
            secondaryUserId = await seeder.CreateShopUserAsync(shopId, "bob", "Bob").ConfigureAwait(true);
            await seeder.CreateProductAsync(productSku, "Film collector", productEan).ConfigureAwait(true);
        }).ConfigureAwait(true);

        var client = CreateClient();

        var startPrimary = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
                new StartRunRequest(shopId, primaryUserId, 1)).ConfigureAwait(true);
        startPrimary.StatusCode.Should().Be(HttpStatusCode.OK);
        var primaryRun = await startPrimary.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(true);
        primaryRun.Should().NotBeNull();
        primaryRun!.OwnerUserId.Should().Be(primaryUserId);

        var completePrimary = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
                new CompleteRunRequest(
                    primaryRun.RunId,
                    primaryUserId,
                    1,
                    new[] { new CompleteRunItemRequest(productEan, 5, false) })).ConfigureAwait(true);
        completePrimary.StatusCode.Should().Be(HttpStatusCode.OK);
        var primarySummary = await completePrimary.Content.ReadFromJsonAsync<CompleteInventoryRunResponse>().ConfigureAwait(true);
        primarySummary.Should().NotBeNull();
        primarySummary!.ItemsCount.Should().Be(1);
        primarySummary.TotalQuantity.Should().Be(5);

        var startSecondary = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
                new StartRunRequest(shopId, secondaryUserId, 2)).ConfigureAwait(true);
        startSecondary.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondaryRun = await startSecondary.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(true);
        secondaryRun.Should().NotBeNull();
        secondaryRun!.OwnerUserId.Should().Be(secondaryUserId);

        var completeMismatch = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
                new CompleteRunRequest(
                    secondaryRun.RunId,
                    secondaryUserId,
                    2,
                    new[] { new CompleteRunItemRequest(productEan, 3, false) })).ConfigureAwait(true);
        completeMismatch.StatusCode.Should().Be(HttpStatusCode.OK);

        var conflictResponse = await client.GetAsync(client.CreateRelativeUri($"/api/conflicts/{locationId}")).ConfigureAwait(true);
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var conflictDetail = await conflictResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(true);
        conflictDetail.Should().NotBeNull();
        conflictDetail!.Items.Should().HaveCount(1);
        var conflictItem = conflictDetail.Items.Single();
        conflictItem.QtyC1.Should().Be(5);
        conflictItem.QtyC2.Should().Be(3);

        var restartSecond = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
                new StartRunRequest(shopId, secondaryUserId, 2)).ConfigureAwait(true);
        restartSecond.StatusCode.Should().Be(HttpStatusCode.OK);
        var restartedRun = await restartSecond.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(true);
        restartedRun.Should().NotBeNull();

        var completeAligned = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
                new CompleteRunRequest(
                    restartedRun!.RunId,
                    secondaryUserId,
                    2,
                    new[] { new CompleteRunItemRequest(productEan, 5, false) })).ConfigureAwait(true);
        completeAligned.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolvedResponse = await client.GetAsync(client.CreateRelativeUri($"/api/conflicts/{locationId}")).ConfigureAwait(true);
        resolvedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolvedDetail = await resolvedResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(true);
        resolvedDetail.Should().NotBeNull();
        resolvedDetail!.Items.Should().BeEmpty();

        var locationsResponse = await client.GetAsync(client.CreateRelativeUri($"/locations?shopId={shopId}")).ConfigureAwait(true);
        locationsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var locations = await locationsResponse.Content.ReadFromJsonAsync<LocationListItemDto[]>().ConfigureAwait(true);
        locations.Should().NotBeNull();
        var nonNullLocations = locations!;
        nonNullLocations.Should().ContainSingle();
        var location = nonNullLocations.Single();
        location.IsBusy.Should().BeFalse();
        location.CountStatuses.Should().NotBeNull();
        location.CountStatuses.Should().HaveCountGreaterThan(0);
        location.CountStatuses.Should().OnlyContain(status => status.CountType == 1 || status.CountType == 2);
    }
}
