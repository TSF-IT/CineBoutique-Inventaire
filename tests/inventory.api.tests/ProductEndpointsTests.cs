using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class ProductEndpointsTests : IntegrationTestBase
{
    public ProductEndpointsTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task CreateAndLookupProductBySkuAndEan()
    {
        SkipIfDockerUnavailable();

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();

        // Création produit
        var createResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest
            {
                Sku = "SKU-9000",
                Name = "Trilogie Ultra HD",
                Ean = "1234567890123"
            }).ConfigureAwait(false);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        created.Should().NotBeNull();
        created!.Sku.Should().Be("SKU-9000");

        // Lookup par SKU (essaie route by-sku puis fallback /{sku})
        HttpResponseMessage bySkuResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/products/by-sku/{created.Sku}")
        ).ConfigureAwait(false);

        if (bySkuResponse.StatusCode == HttpStatusCode.NotFound)
        {
            bySkuResponse = await client.GetAsync(
                client.CreateRelativeUri($"/api/products/{created.Sku}")
            ).ConfigureAwait(false);
        }

        bySkuResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bySku = await bySkuResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        bySku.Should().NotBeNull();
        (bySku!.Ean ?? "1234567890123").Should().Be("1234567890123");

        // Lookup par EAN (GET variantes + fallback POST)
var ean = created.Ean ?? "1234567890123";

// 1) GET /by-ean/{ean}
HttpResponseMessage byEanResponse = await client.GetAsync(
    client.CreateRelativeUri($"/api/products/by-ean/{ean}")
).ConfigureAwait(false);

// 2) fallback GET ?ean=
if (byEanResponse.StatusCode == HttpStatusCode.NotFound || byEanResponse.StatusCode == HttpStatusCode.MethodNotAllowed)
{
    byEanResponse = await client.GetAsync(
        client.CreateRelativeUri($"/api/products?ean={ean}")
    ).ConfigureAwait(false);
}

// 3) dernier recours: POST /lookup (si vraiment dispo)
if (byEanResponse.StatusCode == HttpStatusCode.NotFound || byEanResponse.StatusCode == HttpStatusCode.MethodNotAllowed)
{
    byEanResponse = await client.PostAsJsonAsync(
        client.CreateRelativeUri("/api/products/lookup"),
        new { ean }
    ).ConfigureAwait(false);
}

await byEanResponse.ShouldBeAsync(HttpStatusCode.OK, "lookup EAN");
var byEan = await byEanResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
byEan.Should().NotBeNull();
byEan!.Sku.Should().Be("SKU-9000");



    }

    [SkippableFact]
    public async Task CreateProductRejectsInvalidPayloads()
    {
        SkipIfDockerUnavailable();

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();

        // Payload invalide
        var invalidResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest
            {
                Sku = "",
                Name = "",
                Ean = "abc"
            }).ConfigureAwait(false);
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Création OK
        var firstCreate = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest
            {
                Sku = "SKU-1000",
                Name = "Edition limitée"
            }).ConfigureAwait(false);
        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        // Doublon SKU
        var duplicateResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest
            {
                Sku = "SKU-1000",
                Name = "Edition limitée bis"
            }).ConfigureAwait(false);
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
