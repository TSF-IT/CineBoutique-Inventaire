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
public sealed class ProductEndpointsTests : IntegrationTestBase
{
    public ProductEndpointsTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task CreateAndLookupProductBySkuAndEan()
    {
        SkipIfDockerUnavailable();

        var client = CreateClient();

        var createResponse = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri("/api/products"),
                new CreateProductRequest
                {
                    Sku = "SKU-9000",
                    Name = "Trilogie Ultra HD",
                    Ean = "1234567890123"
                }).ConfigureAwait(true);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(true);
        created.Should().NotBeNull();
        created!.Sku.Should().Be("SKU-9000");

        var bySkuResponse = await client.GetAsync(client.CreateRelativeUri($"/api/products/{created.Sku}")).ConfigureAwait(true);
        bySkuResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bySku = await bySkuResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(true);
        bySku.Should().NotBeNull();
        bySku!.Ean.Should().Be("1234567890123");

        var byEanResponse = await client.GetAsync(client.CreateRelativeUri("/api/products/0001234567890")).ConfigureAwait(true);
        byEanResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var byEan = await byEanResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(true);
        byEan.Should().NotBeNull();
        byEan!.Sku.Should().Be("SKU-9000");
    }

    [SkippableFact]
    public async Task CreateProductRejectsInvalidPayloads()
    {
        SkipIfDockerUnavailable();

        var client = CreateClient();

        var invalidResponse = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri("/api/products"),
                new CreateProductRequest
                {
                    Sku = "",
                    Name = "",
                    Ean = "abc"
                }).ConfigureAwait(true);
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var firstCreate = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri("/api/products"),
                new CreateProductRequest
                {
                    Sku = "SKU-1000",
                    Name = "Edition limitée"
                }).ConfigureAwait(true);
        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicateResponse = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri("/api/products"),
                new CreateProductRequest
                {
                    Sku = "SKU-1000",
                    Name = "Edition limitée bis"
                }).ConfigureAwait(true);
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
