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

        // --- Premier comptage: START -> COMPLETE(+items)
        var startPrimary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, primaryUserId, 1)).ConfigureAwait(false);

        await startPrimary.ShouldBeAsync(HttpStatusCode.OK, "start primary");
        var primaryRun = await startPrimary.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        primaryRun.Should().NotBeNull();
        primaryRun!.OwnerUserId.Should().Be(primaryUserId);

        var completePrimary = await CompleteWithItemsAsync(
            client, locationId, primaryRun.RunId, primaryUserId, 1, productSku, productEan, 5
        ).ConfigureAwait(false);
        await completePrimary.ShouldBeAsync(HttpStatusCode.OK, "complete primary");

        // --- Deuxième comptage (désaccord): START -> COMPLETE(+items)
        var startSecondary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, secondaryUserId, 2)).ConfigureAwait(false);

        await startSecondary.ShouldBeAsync(HttpStatusCode.OK, "start secondary");
        var secondaryRun = await startSecondary.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        secondaryRun.Should().NotBeNull();
        secondaryRun!.OwnerUserId.Should().Be(secondaryUserId);

        var completeMismatch = await CompleteWithItemsAsync(
            client, locationId, secondaryRun.RunId, secondaryUserId, 2, productSku, productEan, 3
        ).ConfigureAwait(false);
        await completeMismatch.ShouldBeAsync(HttpStatusCode.OK, "complete mismatch");

        // --- Lecture des conflits (contrat actuel: conflit présent + C2=3 dans allCounts; C1 peut être non matérialisé)
        var (exists, c1, c2, raw, item) = await ReadConflictAsync(client, locationId, productSku, productEan).ConfigureAwait(false);
        exists.Should().BeTrue($"conflit attendu pour ean={productEan} ou sku={productSku}. Body: {raw}");

        c2.HasValue.Should().BeTrue($"countType=2 (C2) doit être présent. Item: {item}");
        c2!.Value.Should().Be(3, $"C2 doit compter 3 pour le produit. Item: {item}");

        if (c1.HasValue)
            c1!.Value.Should().Be(5, $"C1 doit compter 5 quand il est exposé. Item: {item}");

        // --- Alignement C2 à 5: START -> COMPLETE(+items)
        var restartSecond = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, secondaryUserId, 2)).ConfigureAwait(false);

        await restartSecond.ShouldBeAsync(HttpStatusCode.OK, "restart second");
        var restartedRun = await restartSecond.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        restartedRun.Should().NotBeNull();

        var completeAligned = await CompleteWithItemsAsync(
            client, locationId, restartedRun!.RunId, secondaryUserId, 2, productSku, productEan, 5
        ).ConfigureAwait(false);
        await completeAligned.ShouldBeAsync(HttpStatusCode.OK, "complete aligned");

        // --- Vérifie que le conflit est résolu
        var resolved = await client.GetAsync(client.CreateRelativeUri($"/api/conflicts/{locationId}")).ConfigureAwait(false);
        resolved.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolvedJson = await resolved.Content.ReadAsStringAsync().ConfigureAwait(false);
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
        location.CountStatuses.Should().OnlyContain(status => status.CountType == 1 || status.CountType == 2);
    }

    // ========== Helpers ==========

    // COMPLETE qui transporte les items (contrat effectif chez toi)
    private static Task<HttpResponseMessage> CompleteWithItemsAsync(
        HttpClient client, Guid locationId, Guid runId, Guid ownerUserId, int countType, string sku, string ean, int quantity)
    {
        var uri = client.CreateRelativeUri($"/api/inventories/{locationId}/complete");

        var payload = new
        {
            RunId = runId,
            OwnerUserId = ownerUserId,
            CountType = countType,
            Items = new[]
            {
                new { Sku = sku, Ean = ean, Quantity = quantity, IsDamaged = false }
            }
        };

        return client.PostAsJsonAsync(uri, payload);
    }

    // Retourne: (exists, c1?, c2?, raw, item)
    private static async Task<(bool exists, int? c1, int? c2, string raw, JsonElement item)> ReadConflictAsync(
        HttpClient client, Guid locationId, string sku, string ean)
    {
        var resp = await client.GetAsync(client.CreateRelativeUri($"/api/conflicts/{locationId}")).ConfigureAwait(false);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var items = root.ValueKind == JsonValueKind.Array
            ? root
            : (root.TryGetProperty("items", out var arr) ? arr : default);

        JsonElement item = default;
        var found = false;

        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in items.EnumerateArray())
            {
                var match =
                    (el.TryGetProperty("ean", out var eEl) && eEl.ValueKind == JsonValueKind.String && eEl.GetString() == ean) ||
                    (el.TryGetProperty("sku", out var sEl) && sEl.ValueKind == JsonValueKind.String && sEl.GetString() == sku);

                if (match) { item = el; found = true; break; }
            }
        }

        if (!found) return (false, null, null, json, default);

        int? readC1 = null, readC2 = null;

        if (item.TryGetProperty("qtyC1", out var a) && a.TryGetInt32(out var av)) readC1 = av;
        if (item.TryGetProperty("qtyC2", out var b) && b.TryGetInt32(out var bv)) readC2 = bv;

        if (!readC1.HasValue && item.TryGetProperty("quantityFirstCount", out var a2) && a2.TryGetInt32(out var av2)) readC1 = av2;
        if (!readC2.HasValue && item.TryGetProperty("quantitySecondCount", out var b2) && b2.TryGetInt32(out var bv2)) readC2 = bv2;

        if (item.TryGetProperty("allCounts", out var all) && all.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in all.EnumerateArray())
            {
                if (r.TryGetProperty("countType", out var ct) && ct.TryGetInt32(out var ctv))
                {
                    if (ctv == 1 && !readC1.HasValue)
                    {
                        if (r.TryGetProperty("quantity", out var q1) && q1.TryGetInt32(out var q1v)) readC1 = q1v;
                        else if (r.TryGetProperty("qty", out var q1b) && q1b.TryGetInt32(out var q1vb)) readC1 = q1vb;
                    }
                    if (ctv == 2 && !readC2.HasValue)
                    {
                        if (r.TryGetProperty("quantity", out var q2) && q2.TryGetInt32(out var q2v)) readC2 = q2v;
                        else if (r.TryGetProperty("qty", out var q2b) && q2b.TryGetInt32(out var q2vb)) readC2 = q2vb;
                    }
                }
            }
        }

        return (true, readC1, readC2, json, item);
    }
}
