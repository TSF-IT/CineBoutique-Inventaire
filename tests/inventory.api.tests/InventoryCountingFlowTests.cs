using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using FluentAssertions;
using Xunit;
using CineBoutique.Inventory.Api.Tests.Helpers;


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
            shopId = await seeder.CreateShopAsync("Boutique Tests").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "Z-001", "Zone Pilote").ConfigureAwait(false);
            primaryUserId = await seeder.CreateShopUserAsync(shopId, "alice", "Alice").ConfigureAwait(false);
            secondaryUserId = await seeder.CreateShopUserAsync(shopId, "bob", "Bob").ConfigureAwait(false);
            await seeder.CreateProductAsync(productSku, "Film collector", productEan).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();

        // --- Premier comptage
        var startPrimary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, primaryUserId, 1)).ConfigureAwait(false);

        startPrimary.StatusCode.Should().Be(HttpStatusCode.OK);
        var primaryRun = await startPrimary.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        primaryRun.Should().NotBeNull();
        primaryRun!.OwnerUserId.Should().Be(primaryUserId);

        var completePrimary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
            new CompleteRunRequest(
                primaryRun.RunId,
                primaryUserId,
                1,
                new[] { new CompleteRunItemRequest(productSku, 5, false) })).ConfigureAwait(false);

        completePrimary.StatusCode.Should().Be(HttpStatusCode.OK);

        // --- Second comptage (en désaccord)
        var startSecondary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, secondaryUserId, 2)).ConfigureAwait(false);

        startSecondary.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondaryRun = await startSecondary.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        secondaryRun.Should().NotBeNull();
        secondaryRun!.OwnerUserId.Should().Be(secondaryUserId);

        var completeMismatch = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
            new CompleteRunRequest(
                secondaryRun.RunId,
                secondaryUserId,
                2,
                new[] { new CompleteRunItemRequest(productSku, 3, false) })).ConfigureAwait(false);

        completeMismatch.StatusCode.Should().Be(HttpStatusCode.OK);

        // --- Lecture des conflits, tolérante au schéma
        var conflictResponse = await client.GetAsync(client.CreateRelativeUri($"/api/conflicts/{locationId}")).ConfigureAwait(false);
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await conflictResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var items = root.TryGetProperty("items", out var arr) ? arr.EnumerateArray() : root.EnumerateArray();
        var conflict = items.FirstOrDefault();

        conflict.ValueKind.Should().NotBe(JsonValueKind.Undefined);

        int qtyC1 =
            conflict.TryGetProperty("qtyC1", out var q1) && q1.TryGetInt32(out var v1) ? v1 :
            conflict.TryGetProperty("quantityFirstCount", out var q1b) && q1b.TryGetInt32(out var v1b) ? v1b :
            conflict.TryGetProperty("qtyFirst", out var q1c) && q1c.TryGetInt32(out var v1c) ? v1c : 0;

        int qtyC2 =
            conflict.TryGetProperty("qtyC2", out var q2) && q2.TryGetInt32(out var v2) ? v2 :
            conflict.TryGetProperty("quantitySecondCount", out var q2b) && q2b.TryGetInt32(out var v2b) ? v2b :
            conflict.TryGetProperty("qtySecond", out var q2c) && q2c.TryGetInt32(out var v2c) ? v2c : 0;

        qtyC1.Should().Be(5);
        qtyC2.Should().Be(3);

        // --- Reprise du second comptage (alignement)
        var restartSecond = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, secondaryUserId, 2)).ConfigureAwait(false);

        restartSecond.StatusCode.Should().Be(HttpStatusCode.OK);
        var restartedRun = await restartSecond.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        restartedRun.Should().NotBeNull();

        var completeAligned = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
            new CompleteRunRequest(
                restartedRun!.RunId,
                secondaryUserId,
                2,
                new[] { new CompleteRunItemRequest(productSku, 5, false) })).ConfigureAwait(false);

        completeAligned.StatusCode.Should().Be(HttpStatusCode.OK);

        // --- Vérifie que le conflit est résolu
        var resolvedResponse = await client.GetAsync(client.CreateRelativeUri($"/api/conflicts/{locationId}")).ConfigureAwait(false);
        resolvedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolvedJson = await resolvedResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var resolvedDoc = JsonDocument.Parse(resolvedJson);
        var resolvedRoot = resolvedDoc.RootElement;
        var resolvedItems = resolvedRoot.TryGetProperty("items", out var arr2) ? arr2.EnumerateArray() : resolvedRoot.EnumerateArray();
        resolvedItems.Should().BeEmpty();

        // --- Vérifie que la zone est libérée
        var locationsResponse = await client.GetAsync(client.CreateRelativeUri($"/locations?shopId={shopId}")).ConfigureAwait(false);
        locationsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var locations = await locationsResponse.Content.ReadFromJsonAsync<LocationListItemDto[]>().ConfigureAwait(false);
        locations.Should().NotBeNull();
        var location = locations!.Single();
        location.IsBusy.Should().BeFalse();
        location.CountStatuses.Should().NotBeNullOrEmpty();
        location.CountStatuses.Should().OnlyContain(status =>
    status.CountType == 1 || status.CountType == 2);

    }
}
