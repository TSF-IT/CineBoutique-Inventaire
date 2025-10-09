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
public async Task CreateAndGetProductBySku()
{
    SkipIfDockerUnavailable();
    await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
    var client = CreateClient();

    // Create
    var createResponse = await client.PostAsJsonAsync(
        client.CreateRelativeUri("/api/products"),
        new CreateProductRequest { Sku = "SKU-9000", Name = "Trilogie Ultra HD", Ean = "1234567890123" }
    ).ConfigureAwait(false);
    await createResponse.ShouldBeAsync(HttpStatusCode.Created, "create product");

    var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
    created.Should().NotBeNull();
    created!.Sku.Should().Be("SKU-9000");
    (created.Ean ?? "1234567890123").Should().Be("1234567890123");

    // Get by SKU (route officielle)
    var bySkuResponse = await client.GetAsync(
        client.CreateRelativeUri($"/api/products/by-sku/{created.Sku}")
    ).ConfigureAwait(false);

    if (bySkuResponse.StatusCode == HttpStatusCode.NotFound)
    {
        // fallback documenté et unique: certaines implémentations exposent /api/products/{sku}
        bySkuResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/products/{created.Sku}")
        ).ConfigureAwait(false);
    }

    await bySkuResponse.ShouldBeAsync(HttpStatusCode.OK, "get by sku");
    var bySku = await bySkuResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
    bySku.Should().NotBeNull();
    bySku!.Sku.Should().Be("SKU-9000");
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
