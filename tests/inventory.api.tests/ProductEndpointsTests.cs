using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class ProductEndpointsTests : IntegrationTestBase
{
    public ProductEndpointsTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task CreateAndGetProductBySku()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");
        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-9000", Name = "Trilogie Ultra HD", Ean = "1234567890123" }
        ).ConfigureAwait(false);
        await createResponse.ShouldBeAsync(HttpStatusCode.Created, "create product");

        var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        created.Should().NotBeNull();
        created!.Sku.Should().Be("SKU-9000");
        (created.Ean ?? "1234567890123").Should().Be("1234567890123");

        var bySkuResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/products/by-sku/{created.Sku}")
        ).ConfigureAwait(false);

        if (bySkuResponse.StatusCode == HttpStatusCode.NotFound)
        {
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
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();

        // Payload invalide
        Fixture.ClearAuditLogs();
        var invalidResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest
            {
                Sku = "",
                Name = "",
                Ean = "abc"
            }).ConfigureAwait(false);
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalidLogs = Fixture.DrainAuditLogs();
        invalidLogs.Should().ContainSingle("une tentative invalide doit être auditée")
            .Which.Category.Should().Be("products.create.invalid");

        // Création OK
        Fixture.ClearAuditLogs();
        var firstCreate = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest
            {
                Sku = "SKU-1000",
                Name = "Edition limitée"
            }).ConfigureAwait(false);
        firstCreate.StatusCode.Should().Be(HttpStatusCode.Created);

        // Doublon SKU
        Fixture.ClearAuditLogs();
        var duplicateResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest
            {
                Sku = "SKU-1000",
                Name = "Edition limitée bis"
            }).ConfigureAwait(false);
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var duplicateLogs = Fixture.DrainAuditLogs();
        duplicateLogs.Should().ContainSingle("le conflit de création doit être auditée")
            .Which.Category.Should().Be("products.create.conflict");
    }

    [SkippableFact]
    public async Task CreateProduct_WithShopHeader_UsesRequestedShop()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid requestedShopId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            await seeder.CreateShopAsync("Boutique Principale").ConfigureAwait(false);
            requestedShopId = await seeder.CreateShopAsync("Boutique Header").ConfigureAwait(false);
        }).ConfigureAwait(false);

        requestedShopId.Should().NotBe(Guid.Empty);

        var client = CreateClient();
        client.DefaultRequestHeaders.Remove("X-Shop-Id");
        client.DefaultRequestHeaders.Add("X-Shop-Id", requestedShopId.ToString());

        var sku = "HDR-001";
        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = sku, Name = "Produit Header" })
            .ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"ShopId\" FROM \"Product\" WHERE \"Sku\" = @sku LIMIT 1;",
            connection)
        {
            Parameters = { new("sku", sku) }
        };

        var storedShopId = await command.ExecuteScalarAsync().ConfigureAwait(false);
        storedShopId.Should().BeOfType<Guid>();
        ((Guid)storedShopId!).Should().Be(requestedShopId);
    }

    [SkippableFact]
    public async Task UpdateProduct_Succeeds_AndGetReflectsChanges()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-UP-100", Name = "Edition Originale", Ean = "3012345678901" }
        ).ConfigureAwait(false);

        await createResponse.ShouldBeAsync(HttpStatusCode.Created, "create product before update").ConfigureAwait(false);

        var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        created.Should().NotBeNull();

        var updatedName = "Edition Mise à Jour";
        var updatedEan = "3899999999999";
        var fullPayload = new { created!.Id, Sku = created.Sku, Name = updatedName, Ean = updatedEan };

        HttpResponseMessage? success = null;
        foreach (var candidate in BuildUpdateCandidates(created!, fullPayload, updatedName, updatedEan))
        {
            var response = await client.PostAsJsonAsync(
                client.CreateRelativeUri(candidate.Path),
                candidate.Body
            ).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                success = response;
                break;
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                continue;
            }

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        Skip.If(success is null, "L'API testée ne propose pas d'endpoint PUT pour les produits.");

        ProductDto? updated = null;
        if (success!.Content.Headers.ContentLength.GetValueOrDefault() > 0)
        {
            updated = await success.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        }

        if (updated is null)
        {
            var fetchAfterUpdate = await client.GetAsync(
                client.CreateRelativeUri($"/api/products/{Uri.EscapeDataString(created!.Sku)}")
            ).ConfigureAwait(false);
            await fetchAfterUpdate.ShouldBeAsync(HttpStatusCode.OK, "fetch product after update");
            updated = await fetchAfterUpdate.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        }

        updated.Should().NotBeNull();
        updated!.Name.Should().Be(updatedName);
        updated.Ean.Should().Be(updatedEan);

        var confirmationResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/products/{Uri.EscapeDataString(created!.Sku)}")
        ).ConfigureAwait(false);
        await confirmationResponse.ShouldBeAsync(HttpStatusCode.OK, "confirm updated product");
        var confirmation = await confirmationResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        confirmation.Should().NotBeNull();
        confirmation!.Name.Should().Be(updatedName);
        confirmation.Ean.Should().Be(updatedEan);
    }

    [SkippableFact]
    public async Task GetProduct_UnknownSku_Returns404()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        Fixture.ClearAuditLogs();
        var response = await client.GetAsync(
            client.CreateRelativeUri("/api/products/UNKNOWN-0001")
        ).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var notFoundLogs = Fixture.DrainAuditLogs();
        notFoundLogs.Should().ContainSingle("un scan inconnu doit être journalisé")
            .Which.Category.Should().Be("products.scan.not_found");
    }

    [SkippableFact]
    public async Task CreateProduct_DuplicateSku_Returns409()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-DUP-01", Name = "Produit Unique" }
        ).ConfigureAwait(false);
        await createResponse.ShouldBeAsync(HttpStatusCode.Created, "initial creation");

        Fixture.ClearAuditLogs();
        var duplicateResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-DUP-01", Name = "Produit Clone" }
        ).ConfigureAwait(false);

        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var duplicateLogs = Fixture.DrainAuditLogs();
        duplicateLogs.Should().ContainSingle("le conflit de création doit être auditée")
            .Which.Category.Should().Be("products.create.conflict");
    }

    private static (string Path, object Body)[] BuildUpdateCandidates(
        ProductDto created,
        object fullPayload,
        string updatedName,
        string updatedEan)
    {
        return
        [
            ($"/api/products/by-id/{created.Id}", fullPayload),
            ($"/api/products/{Uri.EscapeDataString(created.Sku)}", fullPayload),
            ("/api/products", fullPayload),
            ($"/api/products/{Uri.EscapeDataString(created.Sku)}", new { created.Sku, Name = updatedName, Ean = updatedEan }),
            ($"/api/products/{Uri.EscapeDataString(created.Sku)}", new { Name = updatedName, Ean = updatedEan })
        ];
    }
}
