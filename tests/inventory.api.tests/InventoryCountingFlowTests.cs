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

        var completePrimary = await PostCompleteAsync(client, locationId, primaryRun.RunId, primaryUserId, 1, productEan, 5);
        await completePrimary.ShouldBeAsync(HttpStatusCode.OK, "complete primary");

        // --- Second comptage (en désaccord)
        var startSecondary = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, secondaryUserId, 2)).ConfigureAwait(false);

        startSecondary.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondaryRun = await startSecondary.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        secondaryRun.Should().NotBeNull();
        secondaryRun!.OwnerUserId.Should().Be(secondaryUserId);

        var completeMismatch = await PostCompleteAsync(client, locationId, secondaryRun.RunId, secondaryUserId, 2, productEan, 3);
        await completeMismatch.ShouldBeAsync(HttpStatusCode.OK, "complete mismatch");

        // --- Lecture des conflits, extraction robuste (match EAN/SKU + schémas multiples)
        var conflictResponse = await client.GetAsync(client.CreateRelativeUri($"/api/conflicts/{locationId}")).ConfigureAwait(false);
        await conflictResponse.ShouldBeAsync(HttpStatusCode.OK, "read conflicts");

        var conflictsJson = await conflictResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var conflictsDoc = JsonDocument.Parse(conflictsJson);
        var root = conflictsDoc.RootElement;

        var itemsEl = root.ValueKind == JsonValueKind.Array
            ? root
            : (root.TryGetProperty("items", out var arrEl) ? arrEl : default);

        JsonElement conflictItemEl = default;
        var foundItem = false;

        if (itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in itemsEl.EnumerateArray())
            {
                var match =
                    (el.TryGetProperty("ean", out var eanEl) && eanEl.ValueKind == JsonValueKind.String && eanEl.GetString() == productEan) ||
                    (el.TryGetProperty("sku", out var skuEl) && skuEl.ValueKind == JsonValueKind.String && skuEl.GetString() == productSku);

                if (match)
                {
                    conflictItemEl = el;
                    foundItem = true;
                    break;
                }
            }
        }

        foundItem.Should().BeTrue($"le conflit pour EAN={productEan} ou SKU={productSku} doit exister. Body: {conflictsJson}");

        static int ReadCountQuantity(JsonElement el, int countType)
        {
            // Schéma 1: champs plats qtyC1/qtyC2
            if (countType == 1 && el.TryGetProperty("qtyC1", out var c1) && c1.TryGetInt32(out var v1)) return v1;
            if (countType == 2 && el.TryGetProperty("qtyC2", out var c2) && c2.TryGetInt32(out var v2)) return v2;

            // Schéma 2: quantityFirstCount / quantitySecondCount
            if (countType == 1 && el.TryGetProperty("quantityFirstCount", out var q1) && q1.TryGetInt32(out var vv1)) return vv1;
            if (countType == 2 && el.TryGetProperty("quantitySecondCount", out var q2) && q2.TryGetInt32(out var vv2)) return vv2;

            // Schéma 3: firstCount { quantity }, secondCount { quantity }
            if (countType == 1 && el.TryGetProperty("firstCount", out var fc) && fc.ValueKind == JsonValueKind.Object
                && fc.TryGetProperty("quantity", out var fcq) && fcq.TryGetInt32(out var vfcq)) return vfcq;
            if (countType == 2 && el.TryGetProperty("secondCount", out var sc) && sc.ValueKind == JsonValueKind.Object
                && sc.TryGetProperty("quantity", out var scq) && scq.TryGetInt32(out var vscq)) return vscq;

            // Schéma 4: counts: [{ countType: 1|2, quantity/qty: n }]
            if (el.TryGetProperty("counts", out var counts) && counts.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in counts.EnumerateArray())
                {
                    if (c.TryGetProperty("countType", out var ct) && ct.TryGetInt32(out var ctVal) && ctVal == countType)
                    {
                        if (c.TryGetProperty("quantity", out var cq) && cq.TryGetInt32(out var vv)) return vv;
                        if (c.TryGetProperty("qty", out var cq2) && cq2.TryGetInt32(out var vv2b)) return vv2b;
                    }
                }
            }

            // Schéma 5: runs: [{ countType, items: [{ ean/sku, quantity }] }]
            if (el.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in runs.EnumerateArray())
                {
                    if (r.TryGetProperty("countType", out var rct) && rct.TryGetInt32(out var rctVal) && rctVal == countType)
                    {
                        if (r.TryGetProperty("items", out var ritems) && ritems.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var it in ritems.EnumerateArray())
                            {
                                // si items portent l’ean, on matche
                                if (it.TryGetProperty("ean", out var itean) && itean.ValueKind == JsonValueKind.String)
                                {
                                    if (!el.TryGetProperty("ean", out var elEan) || elEan.GetString() != itean.GetString())
                                        continue;
                                }
                                if (it.TryGetProperty("quantity", out var rq) && rq.TryGetInt32(out var rv)) return rv;
                                if (it.TryGetProperty("qty", out var rq2) && rq2.TryGetInt32(out var rv2)) return rv2;
                            }
                        }
                    }
                }
            }

            return 0;
        }

        var qtyC1 = ReadCountQuantity(conflictItemEl, 1);
        var qtyC2 = ReadCountQuantity(conflictItemEl, 2);

        qtyC1.Should().Be(5, $"C1 doit compter 5 pour le produit. Item: {conflictItemEl}");
        qtyC2.Should().Be(3, $"C2 doit compter 3 pour le produit. Item: {conflictItemEl}");

        // --- Reprise du second comptage (alignement)
        var restartSecond = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{locationId}/start"),
            new StartRunRequest(shopId, secondaryUserId, 2)).ConfigureAwait(false);

        restartSecond.StatusCode.Should().Be(HttpStatusCode.OK);
        var restartedRun = await restartSecond.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        restartedRun.Should().NotBeNull();

        var completeAligned = await PostCompleteAsync(client, locationId, restartedRun!.RunId, secondaryUserId, 2, productEan, 5);
        await completeAligned.ShouldBeAsync(HttpStatusCode.OK, "complete aligned");

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
        location.CountStatuses.Should().OnlyContain(status => status.CountType == 1 || status.CountType == 2);
    }

    private static Task<HttpResponseMessage> PostCompleteAsync(
        HttpClient client, Guid locationId, Guid runId, Guid ownerUserId, int countType, string ean, int quantity)
    {
        var uri = client.CreateRelativeUri($"/api/inventories/{locationId}/complete");

        // CONTRAT annoncé: OwnerUserId + CountType + Items[] avec Ean obligatoire
        var payload = new
        {
            RunId = runId,
            OwnerUserId = ownerUserId,
            CountType = countType,
            Items = new[] { new { Ean = ean, Quantity = quantity, IsDamaged = false } }
        };

        return client.PostAsJsonAsync(uri, payload);
    }
}
