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

        // --- Premier comptage: START -> ADD ITEMS -> COMPLETE
        var startPrimary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, primaryUserId, 1)).ConfigureAwait(false);

        await startPrimary.ShouldBeAsync(HttpStatusCode.OK, "start primary");
        var primaryRun = await startPrimary.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        primaryRun.Should().NotBeNull();
        primaryRun!.OwnerUserId.Should().Be(primaryUserId);

        await AddItemsAsync(client, locationId, primaryRun.RunId, primaryUserId, 1, productSku, productEan, 5)
            .ConfigureAwait(false);

        var completePrimary = await CompleteAsync(client, locationId, primaryRun.RunId, primaryUserId, 1).ConfigureAwait(false);
        await completePrimary.ShouldBeAsync(HttpStatusCode.OK, "complete primary");

        // --- Deuxième comptage (désaccord): START -> ADD ITEMS -> COMPLETE
        var startSecondary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, secondaryUserId, 2)).ConfigureAwait(false);

        await startSecondary.ShouldBeAsync(HttpStatusCode.OK, "start secondary");
        var secondaryRun = await startSecondary.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        secondaryRun.Should().NotBeNull();
        secondaryRun!.OwnerUserId.Should().Be(secondaryUserId);

        await AddItemsAsync(client, locationId, secondaryRun.RunId, secondaryUserId, 2, productSku, productEan, 3)
            .ConfigureAwait(false);

        var completeMismatch = await CompleteAsync(client, locationId, secondaryRun.RunId, secondaryUserId, 2).ConfigureAwait(false);
        await completeMismatch.ShouldBeAsync(HttpStatusCode.OK, "complete mismatch");

        // --- Lecture des conflits (robuste)
        var (qtyC1, qtyC2) = await ReadConflictQuantitiesAsync(client, locationId, productSku, productEan).ConfigureAwait(false);
        qtyC1.Should().Be(5, $"C1 doit compter 5 pour le produit.");
        qtyC2.Should().Be(3, $"C2 doit compter 3 pour le produit.");

        // --- Alignement C2 à 5: START -> ADD ITEMS -> COMPLETE
        var restartSecond = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, secondaryUserId, 2)).ConfigureAwait(false);

        await restartSecond.ShouldBeAsync(HttpStatusCode.OK, "restart second");
        var restartedRun = await restartSecond.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        restartedRun.Should().NotBeNull();

        await AddItemsAsync(client, locationId, restartedRun!.RunId, secondaryUserId, 2, productSku, productEan, 5)
            .ConfigureAwait(false);

        var completeAligned = await CompleteAsync(client, locationId, restartedRun.RunId, secondaryUserId, 2).ConfigureAwait(false);
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

    // Ajoute les items au run AVANT de compléter (essaie 2 formes d'URL usuelles)
    private static async Task AddItemsAsync(HttpClient client, Guid locationId, Guid runId, Guid userId, int countType, string sku, string ean, int quantity)
    {
        var payload = new
        {
            RunId = runId,
            OwnerUserId = userId,
            CountType = countType,
            Items = new[] { new { Sku = sku, Ean = ean, Quantity = quantity, IsDamaged = false } }
        };

        // Forme A: items adressés par location
        var res = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/items"), payload
        ).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            // Forme B: items adressés par run
            res = await client.PostAsJsonAsync(
                client.CreateRelativeUri($"/api/inventories/runs/{runId}/items"), payload
            ).ConfigureAwait(false);
        }

        await res.ShouldBeAsync(HttpStatusCode.OK, "add items");
    }

    // Finalise un run SANS items (contrat typique)
    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid locationId, Guid runId, Guid ownerUserId, int countType)
    {
        var uri = client.CreateRelativeUri($"/api/inventories/{locationId}/complete");
        var payload = new
        {
            RunId = runId,
            OwnerUserId = ownerUserId,
            CountType = countType
        };
        return client.PostAsJsonAsync(uri, payload);
    }

    // Lecture robustes des quantités C1/C2 depuis /api/conflicts/{locationId}
    private static async Task<(int c1, int c2)> ReadConflictQuantitiesAsync(HttpClient client, Guid locationId, string sku, string ean)
    {
        var conflicts = await client.GetAsync(client.CreateRelativeUri($"/api/conflicts/{locationId}")).ConfigureAwait(false);
        conflicts.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await conflicts.Content.ReadAsStringAsync().ConfigureAwait(false);
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

        found.Should().BeTrue($"conflit attendu pour ean={ean} ou sku={sku}. Body: {json}");

        static int read(JsonElement el, int ct)
        {
            if (ct == 1 && el.TryGetProperty("qtyC1", out var a) && a.TryGetInt32(out var av)) return av;
            if (ct == 2 && el.TryGetProperty("qtyC2", out var b) && b.TryGetInt32(out var bv)) return bv;
            if (ct == 1 && el.TryGetProperty("quantityFirstCount", out var a2) && a2.TryGetInt32(out var av2)) return av2;
            if (ct == 2 && el.TryGetProperty("quantitySecondCount", out var b2) && b2.TryGetInt32(out var bv2)) return bv2;

            if (el.TryGetProperty("allCounts", out var all) && all.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in all.EnumerateArray())
                {
                    if (r.TryGetProperty("countType", out var ctEl) && ctEl.TryGetInt32(out var ctv) && ctv == ct)
                    {
                        if (r.TryGetProperty("quantity", out var q) && q.TryGetInt32(out var qv)) return qv;
                        if (r.TryGetProperty("qty", out var q2) && q2.TryGetInt32(out var qv2)) return qv2;
                    }
                }
            }
            return 0;
        }

        var c1 = read(item, 1);
        var c2 = read(item, 2);
        return (c1, c2);
    }
}
