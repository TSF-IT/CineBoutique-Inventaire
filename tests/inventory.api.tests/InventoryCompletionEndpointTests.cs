using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class InventoryCompletionEndpointTests : InventoryApiTestBase
{
    public InventoryCompletionEndpointTests(PostgresTestContainerFixture postgres)
        : base(postgres)
    {
    }

    [Fact]
    public async Task CompleteInventoryRun_ReturnsBadRequest_WhenItemsMissing()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Completion-invalid")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("S1").WithLabel("Zone S1")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("amelie").WithDisplayName("Amélie"))
            .ConfigureAwait(false);

        var payload = new CompleteRunRequest(null, user.Id, 1, Array.Empty<CompleteRunItemRequest>());

        var response = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/complete", payload).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompleteInventoryRun_ReturnsNotFound_WhenRunUnknown()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Completion-notfound")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("S1").WithLabel("Zone S1")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("amelie").WithDisplayName("Amélie"))
            .ConfigureAwait(false);

        var payload = new CompleteRunRequest(Guid.NewGuid(), user.Id, 1, new[] { new CompleteRunItemRequest("12345678", 1m, false) });

        var response = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/complete", payload).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CompleteInventoryRun_CreatesNewRunAndLines()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Completion-create")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("S2").WithLabel("Zone S2")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("camille").WithDisplayName("Camille"))
            .ConfigureAwait(false);
        var product = await Data.CreateProductAsync(builder => builder.WithEan("12345678").WithSku("SKU-001").WithName("Produit référencé"))
            .ConfigureAwait(false);

        var payload = new CompleteRunRequest(
            null,
            user.Id,
            1,
            new[] { new CompleteRunItemRequest("12345678", 2m, false) });

        var response = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/complete", payload).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CompleteInventoryRunResponse>().ConfigureAwait(false);
        Assert.NotNull(result);
        Assert.Equal(location.Id, result!.LocationId);
        Assert.Equal(1, result.ItemsCount);
        Assert.Equal(2m, result.TotalQuantity);

        var detailResponse = await Client.GetAsync($"/api/inventories/runs/{result.RunId}").ConfigureAwait(false);
        detailResponse.EnsureSuccessStatusCode();
        var detail = await detailResponse.Content.ReadFromJsonAsync<CompletedRunDetailDto>().ConfigureAwait(false);
        Assert.NotNull(detail);
        var line = Assert.Single(detail!.Items);
        Assert.Equal(product.Id, line.ProductId);
        Assert.Equal(2m, line.Quantity);
    }

    [Fact]
    public async Task CompleteInventoryRun_UpdatesExistingRun()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Completion-existing")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("S3").WithLabel("Zone S3")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("chloe").WithDisplayName("Chloé"))
            .ConfigureAwait(false);

        var startPayload = new StartRunRequest(shop.Id, user.Id, 1);
        var startResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/start", startPayload).ConfigureAwait(false);
        startResponse.EnsureSuccessStatusCode();
        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        Assert.NotNull(started);

        var ean = "98765432";
        var completePayload = new CompleteRunRequest(
            started!.RunId,
            user.Id,
            1,
            new[] { new CompleteRunItemRequest(ean, 3m, false) });

        var completeResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/complete", completePayload).ConfigureAwait(false);
        completeResponse.EnsureSuccessStatusCode();

        var result = await completeResponse.Content.ReadFromJsonAsync<CompleteInventoryRunResponse>().ConfigureAwait(false);
        Assert.NotNull(result);
        Assert.Equal(started.RunId, result!.RunId);

        var detailResponse = await Client.GetAsync($"/api/inventories/runs/{started.RunId}").ConfigureAwait(false);
        detailResponse.EnsureSuccessStatusCode();
        var detail = await detailResponse.Content.ReadFromJsonAsync<CompletedRunDetailDto>().ConfigureAwait(false);
        Assert.NotNull(detail);
        var item = Assert.Single(detail!.Items);
        Assert.Equal(ean, item.Ean);
        Assert.Equal(3m, item.Quantity);
    }

    [Fact]
    public async Task CompleteInventoryRun_CreatesConflicts_WhenCountsMismatch()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Completion-conflict")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("S4").WithLabel("Zone S4")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("dorian").WithDisplayName("Dorian"))
            .ConfigureAwait(false);
        await Data.CreateProductAsync(builder => builder.WithEan("55555555").WithSku("SKU-555").WithName("Produit 555"))
            .ConfigureAwait(false);

        var run1Payload = new CompleteRunRequest(null, user.Id, 1, new[] { new CompleteRunItemRequest("55555555", 5m, false) });
        var run2Payload = new CompleteRunRequest(null, user.Id, 2, new[] { new CompleteRunItemRequest("55555555", 8m, false) });

        var firstResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/complete", run1Payload).ConfigureAwait(false);
        firstResponse.EnsureSuccessStatusCode();
        var secondResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/complete", run2Payload).ConfigureAwait(false);
        secondResponse.EnsureSuccessStatusCode();

        var conflictResponse = await Client.GetAsync($"/api/conflicts/{location.Id}").ConfigureAwait(false);
        conflictResponse.EnsureSuccessStatusCode();
        var conflict = await conflictResponse.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        Assert.NotNull(conflict);
        var item = Assert.Single(conflict!.Items);
        Assert.Equal(5, item.QtyC1);
        Assert.Equal(8, item.QtyC2);
        Assert.Equal(-3, item.Delta);
    }

    [Fact]
    public async Task CompleteInventoryRun_ResolvesConflicts_WhenLoopMatchesPrevious()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Completion-loop")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("S5").WithLabel("Zone S5")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("edouard").WithDisplayName("Édouard"))
            .ConfigureAwait(false);
        await Data.CreateProductAsync(builder => builder.WithEan("99999999").WithSku("SKU-999").WithName("Produit 999"))
            .ConfigureAwait(false);

        var run1Payload = new CompleteRunRequest(null, user.Id, 1, new[] { new CompleteRunItemRequest("99999999", 10m, false) });
        var run2Payload = new CompleteRunRequest(null, user.Id, 2, new[] { new CompleteRunItemRequest("99999999", 7m, false) });
        var run3Payload = new CompleteRunRequest(null, user.Id, 3, new[] { new CompleteRunItemRequest("99999999", 10m, false) });

        await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/complete", run1Payload).ConfigureAwait(false);
        await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/complete", run2Payload).ConfigureAwait(false);

        var conflictBefore = await Client.GetAsync($"/api/conflicts/{location.Id}").ConfigureAwait(false);
        conflictBefore.EnsureSuccessStatusCode();
        var payloadBefore = await conflictBefore.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        Assert.NotNull(payloadBefore);
        Assert.NotEmpty(payloadBefore!.Items);

        await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/complete", run3Payload).ConfigureAwait(false);

        var conflictAfter = await Client.GetAsync($"/api/conflicts/{location.Id}").ConfigureAwait(false);
        conflictAfter.EnsureSuccessStatusCode();
        var payloadAfter = await conflictAfter.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        Assert.NotNull(payloadAfter);
        Assert.Empty(payloadAfter!.Items);

        var summaryResponse = await Client.GetAsync($"/api/inventories/summary?shopId={shop.Id:D}").ConfigureAwait(false);
        summaryResponse.EnsureSuccessStatusCode();
        var summary = await summaryResponse.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        Assert.NotNull(summary);
        Assert.Equal(0, summary!.Conflicts);
    }
}
