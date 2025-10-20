using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class OpenApiContractTests : IntegrationTestBase
{
    public OpenApiContractTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }
    private static readonly string[] expectation = new[] { "sku", "name" };

    [SkippableFact]
    public async Task Swagger_ExposeExpectedProductContract()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var response = await client.GetAsync(client.CreateRelativeUri("/swagger/v1/swagger.json")).ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "le document OpenAPI doit être accessible").ConfigureAwait(false);

        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var document = new OpenApiStringReader().Read(payload, out var diagnostic);

        diagnostic.Should().NotBeNull();
        diagnostic!.Errors.Should().BeEmpty("le document OpenAPI doit être valide");

        var paths = document.Paths;

        paths.Should().ContainKey("/api/products");
        AssertOperation(paths["/api/products"], OperationType.Post, "CreateProduct", "201", "400", "409");

        paths.Should().ContainKey("/api/products/{code}");
        var productByCodePath = paths["/api/products/{code}"];
        AssertOperation(productByCodePath, OperationType.Get, "GetProductByCode", "200", "404", "409");
        AssertOperation(productByCodePath, OperationType.Post, "UpdateProductBySkuPost", "200", "400", "404", "409");
        AssertOperation(productByCodePath, OperationType.Put, "UpdateProductBySku", "200", "400", "404", "409");

        paths.Should().ContainKey("/api/products/search");
        AssertOperation(paths["/api/products/search"], OperationType.Get, "SearchProducts", "200", "400");

        paths.Should().ContainKey("/api/products/by-id/{id}");
        AssertOperation(paths["/api/products/by-id/{id}"], OperationType.Post, "UpdateProductByIdPost", "200", "400", "404", "409");
        AssertOperation(paths["/api/products/by-id/{id}"], OperationType.Put, "UpdateProductById", "200", "400", "404", "409");

        document.Components.Schemas.Should().ContainKey("ProductDto");
        var productSchema = document.Components.Schemas["ProductDto"];

        productSchema.Type.Should().Be("object");
        productSchema.Required.Should().BeEquivalentTo(new[] { "id", "sku", "name" });
        productSchema.Properties.Should().ContainKeys("id", "sku", "name", "ean");

        var idSchema = productSchema.Properties["id"];
        idSchema.Type.Should().Be("string");
        idSchema.Format.Should().Be("uuid");

        var skuSchema = productSchema.Properties["sku"];
        skuSchema.Type.Should().Be("string");
        skuSchema.Nullable.Should().BeFalse();

        var nameSchema = productSchema.Properties["name"];
        nameSchema.Type.Should().Be("string");
        nameSchema.Nullable.Should().BeFalse();

        var eanSchema = productSchema.Properties["ean"];
        eanSchema.Type.Should().Be("string");
        eanSchema.Nullable.Should().BeTrue();

        document.Components.Schemas.Should().ContainKey("ProductSearchItemDto");
        var searchSchema = document.Components.Schemas["ProductSearchItemDto"];
        searchSchema.Type.Should().Be("object");
        searchSchema.Required.Should().BeEquivalentTo(expectation);
        searchSchema.Properties.Should().ContainKeys("sku", "code", "name");
        searchSchema.Properties["sku"].Nullable.Should().BeFalse();
        searchSchema.Properties["code"].Nullable.Should().BeTrue();
        searchSchema.Properties["name"].Nullable.Should().BeFalse();
    }

    private static void AssertOperation(OpenApiPathItem pathItem, OperationType operationType, string expectedOperationId, params string[] expectedStatusCodes)
    {
        pathItem.Should().NotBeNull();
        pathItem.Operations.Should().ContainKey(operationType);
        var operation = pathItem.Operations[operationType];

        operation.Should().NotBeNull();
        operation.OperationId.Should().Be(expectedOperationId);
        operation.Responses.Keys.Should().BeEquivalentTo(expectedStatusCodes, options => options.WithoutStrictOrdering());
    }
}
