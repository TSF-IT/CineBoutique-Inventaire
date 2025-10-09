using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class InventoryEntriesAndSummaryTests : IntegrationTestBase
{
    public InventoryEntriesAndSummaryTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task PostEntries_ThenRunDetailsAggregateByProduct()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        Guid locationId = Guid.Empty;
        Guid primaryUserId = Guid.Empty;
        Guid secondaryUserId = Guid.Empty;
        const string sku1 = "SKU-001";
        const string ean1 = "1234567890123";
        const string sku2 = "SKU-002";
        const string ean2 = "2345678901234";

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Agrégats").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "LOC-AGG", "Zone Agrégats").ConfigureAwait(false);
            primaryUserId = await seeder.CreateShopUserAsync(shopId, "alice", "Alice").ConfigureAwait(false);
            secondaryUserId = await seeder.CreateShopUserAsync(shopId, "bob", "Bob").ConfigureAwait(false);
            await seeder.CreateProductAsync(sku1, "Produit 1", ean1).ConfigureAwait(false);
            await seeder.CreateProductAsync(sku2, "Produit 2", ean2).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();

        var startPrimary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, primaryUserId, 1)
        ).ConfigureAwait(false);
        await startPrimary.ShouldBeAsync(HttpStatusCode.OK, "start first run");

        var primaryRun = await startPrimary.Content
            .ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        primaryRun.Should().NotBeNull();

        const decimal quantity = 4m;
        var completePrimary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
            new CompleteRunRequest(
                primaryRun!.RunId,
                primaryUserId,
                1,
                new[]
                {
                    new CompleteRunItemRequest(ean1, quantity, false),
                    new CompleteRunItemRequest(ean2, quantity, false)
                }
            )
        ).ConfigureAwait(false);
        await completePrimary.ShouldBeAsync(HttpStatusCode.OK, "complete first run");

        var startSecondary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, secondaryUserId, 2)
        ).ConfigureAwait(false);
        await startSecondary.ShouldBeAsync(HttpStatusCode.OK, "start second run");

        var secondaryRun = await startSecondary.Content
            .ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        secondaryRun.Should().NotBeNull();

        var completeSecondary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
            new CompleteRunRequest(
                secondaryRun!.RunId,
                secondaryUserId,
                2,
                new[]
                {
                    new CompleteRunItemRequest(ean1, quantity, false),
                    new CompleteRunItemRequest(ean2, quantity, false)
                }
            )
        ).ConfigureAwait(false);
        await completeSecondary.ShouldBeAsync(HttpStatusCode.OK, "complete second run");

        async Task AssertRunDetailAsync(Guid runId, short expectedCountType)
        {
            var detailResponse = await client.GetAsync(
                client.CreateRelativeUri($"/api/inventories/runs/{runId}")
            ).ConfigureAwait(false);
            await detailResponse.ShouldBeAsync(HttpStatusCode.OK, "fetch run detail");

            var detail = await detailResponse.Content
                .ReadFromJsonAsync<CompletedRunDetailDto>().ConfigureAwait(false);
            detail.Should().NotBeNull();
            detail!.CountType.Should().Be(expectedCountType);
            detail.Items.Should().NotBeNull();
            detail.Items.Should().HaveCount(2, "two distinct products were counted");

            var item1 = detail.Items.Single(item => string.Equals(item.Ean, ean1, StringComparison.Ordinal));
            item1.Sku.Should().Be(sku1);
            item1.Quantity.Should().Be(quantity);

            var item2 = detail.Items.Single(item => string.Equals(item.Ean, ean2, StringComparison.Ordinal));
            item2.Sku.Should().Be(sku2);
            item2.Quantity.Should().Be(quantity);
        }

        await AssertRunDetailAsync(primaryRun.RunId, 1).ConfigureAwait(false);
        await AssertRunDetailAsync(secondaryRun.RunId, 2).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task CompleteRun_BlocksFurtherEntries()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        Guid locationId = Guid.Empty;
        Guid operatorId = Guid.Empty;
        const string sku = "SKU-LOCK";
        const string ean = "3456789012345";

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Blocage").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "LOC-BLOCK", "Zone Blocage").ConfigureAwait(false);
            operatorId = await seeder.CreateShopUserAsync(shopId, "carol", "Carol").ConfigureAwait(false);
            await seeder.CreateProductAsync(sku, "Produit Blocage", ean).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        var startResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, operatorId, 1)
        ).ConfigureAwait(false);
        await startResponse.ShouldBeAsync(HttpStatusCode.OK, "start run");

        var started = await startResponse.Content
            .ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();

        var firstCompletion = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
            new CompleteRunRequest(
                started!.RunId,
                operatorId,
                1,
                new[] { new CompleteRunItemRequest(ean, 3m, false) }
            )
        ).ConfigureAwait(false);
        await firstCompletion.ShouldBeAsync(HttpStatusCode.OK, "initial completion");

        var snapshotResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/runs/{started.RunId}")
        ).ConfigureAwait(false);
        await snapshotResponse.ShouldBeAsync(HttpStatusCode.OK, "baseline detail");

        var snapshot = await snapshotResponse.Content
            .ReadFromJsonAsync<CompletedRunDetailDto>().ConfigureAwait(false);
        snapshot.Should().NotBeNull();
        snapshot!.Items.Should().ContainSingle();
        var originalQuantity = snapshot.Items.Single().Quantity;

        var retryCompletion = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/complete"),
            new CompleteRunRequest(
                started.RunId,
                operatorId,
                1,
                new[] { new CompleteRunItemRequest(ean, 7m, false) }
            )
        ).ConfigureAwait(false);

        retryCompletion.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var afterRetry = await client.GetAsync(
            client.CreateRelativeUri($"/api/inventories/runs/{started.RunId}")
        ).ConfigureAwait(false);
        await afterRetry.ShouldBeAsync(HttpStatusCode.OK, "detail after rejected retry");

        var detail = await afterRetry.Content
            .ReadFromJsonAsync<CompletedRunDetailDto>().ConfigureAwait(false);
        detail.Should().NotBeNull();
        detail!.Items.Should().ContainSingle();
        detail.Items.Single().Quantity.Should().Be(originalQuantity, "failed retry must not alter stored lines");
    }
}
