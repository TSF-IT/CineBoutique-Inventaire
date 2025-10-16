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
public sealed class ProductEndpoints_ValidationAndConcurrencyTests : IntegrationTestBase
{
    public ProductEndpoints_ValidationAndConcurrencyTests(InventoryApiFixture fx)
    {
        UseFixture(fx);
    }

    [SkippableFact]
    public async Task CreateProduct_RespectsSkuLengthBoundaries()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var maxSku = new string('S', 32);
        var okResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = maxSku, Name = "Produit 32", Ean = "12345678" }
        ).ConfigureAwait(false);

        await okResponse.ShouldBeAsync(HttpStatusCode.Created, "un SKU de 32 caractères doit être accepté").ConfigureAwait(false);

        var tooLongSku = new string('S', 33);
        var koResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = tooLongSku, Name = "Produit 33", Ean = "87654321" }
        ).ConfigureAwait(false);

        await koResponse.ShouldBeAsync(HttpStatusCode.BadRequest, "un SKU de 33 caractères doit être rejeté").ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task CreateProduct_RespectsNameLengthBoundaries()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var maxName = new string('N', 256);
        var okResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-NAME-OK", Name = maxName, Ean = "12345679" }
        ).ConfigureAwait(false);

        await okResponse.ShouldBeAsync(HttpStatusCode.Created, "un nom de 256 caractères doit être accepté").ConfigureAwait(false);

        var tooLongName = new string('N', 257);
        var koResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-NAME-KO", Name = tooLongName, Ean = "22345679" }
        ).ConfigureAwait(false);

        await koResponse.ShouldBeAsync(HttpStatusCode.BadRequest, "un nom de 257 caractères doit être rejeté").ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task CreateProduct_ValidatesEanFormats()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var shortResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-EAN-7", Name = "EAN court", Ean = "1234567" }
        ).ConfigureAwait(false);
        await shortResponse.ShouldBeAsync(HttpStatusCode.BadRequest, "un EAN de 7 chiffres doit être rejeté").ConfigureAwait(false);

        var ean8 = "12345678";
        var ean8Response = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-EAN-8", Name = "EAN8", Ean = ean8 }
        ).ConfigureAwait(false);
        await ean8Response.ShouldBeAsync(HttpStatusCode.Created, "un EAN de 8 chiffres doit être accepté").ConfigureAwait(false);
        var ean8Product = await ean8Response.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        ean8Product.Should().NotBeNull();
        ean8Product!.Ean.Should().Be(ean8);

        var ean13 = "9876543210123";
        var ean13Response = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-EAN-13", Name = "EAN13", Ean = ean13 }
        ).ConfigureAwait(false);
        await ean13Response.ShouldBeAsync(HttpStatusCode.Created, "un EAN de 13 chiffres doit être accepté").ConfigureAwait(false);

        var longResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-EAN-14", Name = "EAN long", Ean = "12345678901234" }
        ).ConfigureAwait(false);
        await longResponse.ShouldBeAsync(HttpStatusCode.BadRequest, "un EAN de 14 chiffres doit être rejeté").ConfigureAwait(false);

        var nonNumericResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-EAN-ALPHA", Name = "EAN mixte", Ean = "12345ABC" }
        ).ConfigureAwait(false);
        await nonNumericResponse.ShouldBeAsync(HttpStatusCode.BadRequest, "un EAN non numérique doit être rejeté").ConfigureAwait(false);

        var trimmedResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-EAN-TRIM", Name = "EAN trim", Ean = " 87654321 " }
        ).ConfigureAwait(false);
        await trimmedResponse.ShouldBeAsync(HttpStatusCode.Created, "un EAN valide entouré d'espaces doit être accepté").ConfigureAwait(false);
        var trimmedProduct = await trimmedResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        trimmedProduct.Should().NotBeNull();
        trimmedProduct!.Ean.Should().Be("87654321");
    }

    [SkippableFact]
    public async Task CreateProduct_TrimsSkuNameAndEan()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "  SKU-TRIM  ", Name = "  Film  ", Ean = " 12345688 " }
        ).ConfigureAwait(false);

        await response.ShouldBeAsync(HttpStatusCode.Created, "les espaces en entrée doivent être ignorés").ConfigureAwait(false);
        var created = await response.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        created.Should().NotBeNull();
        created!.Sku.Should().Be("SKU-TRIM");
        created.Name.Should().Be("Film");
        created.Ean.Should().Be("12345688");

        Fixture.ClearAuditLogs();
        var fetchResponse = await client.GetAsync(client.CreateRelativeUri("/api/products/SKU-TRIM")).ConfigureAwait(false);
        await fetchResponse.ShouldBeAsync(HttpStatusCode.OK, "le produit doit être récupérable avec le SKU trimé").ConfigureAwait(false);
        var fetched = await fetchResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        fetched.Should().NotBeNull();
        fetched!.Sku.Should().Be("SKU-TRIM");
        fetched.Name.Should().Be("Film");
        fetched.Ean.Should().Be("12345688");
        var successLogs = Fixture.DrainAuditLogs();
        successLogs.Should().ContainSingle("le scan réussi doit être journalisé")
            .Which.Category.Should().Be("products.scan.success");
    }

    [SkippableFact]
    public async Task CreateProduct_ReturnsEncodedLocationHeader()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU 1/2", Name = "Produit spécial", Ean = "12345689" }
        ).ConfigureAwait(false);

        await response.ShouldBeAsync(HttpStatusCode.Created, "la création doit réussir").ConfigureAwait(false);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.IsAbsoluteUri.Should().BeFalse("l'API doit renvoyer une URI relative");
        response.Headers.Location.OriginalString.Should().Be("/api/products/SKU%201%2F2");
    }

    [SkippableFact]
    public async Task CreateProduct_SameEanInParallel_AllowsAllSuccess()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        const string ean = "1122334455667";
        var tasks = Enumerable
            .Range(0, 5)
            .Select(index => client.PostAsJsonAsync(
                client.CreateRelativeUri("/api/products"),
                new CreateProductRequest { Sku = $"SKU-PARALLEL-{index}", Name = $"Produit {index}", Ean = ean }
            ))
            .ToArray();

        var responses = await Task.WhenAll(tasks).ConfigureAwait(false);
        responses.Should().HaveCount(5, "les cinq créations doivent retourner une réponse");
        responses.Should().OnlyContain(
            r => r.StatusCode == HttpStatusCode.Created,
            "le même EAN peut être partagé entre plusieurs produits"
        );
    }

    [SkippableFact]
    public async Task CreateProduct_SameSkuInParallel_AllowsSingleSuccess()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        const string sku = "SKU-PARALLEL";
        var tasks = Enumerable
            .Range(0, 5)
            .Select(index => client.PostAsJsonAsync(
                client.CreateRelativeUri("/api/products"),
                new CreateProductRequest { Sku = sku, Name = $"Produit {index}", Ean = $"9900{index:0000}" }
            ))
            .ToArray();

        var responses = await Task.WhenAll(tasks).ConfigureAwait(false);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "une seule création doit réussir");
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(4, "les autres créations doivent échouer en conflit");

        var fetchResponse = await client.GetAsync(client.CreateRelativeUri($"/api/products/{sku}")).ConfigureAwait(false);
        await fetchResponse.ShouldBeAsync(HttpStatusCode.OK, "le produit créé doit être récupérable").ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task UpdateProduct_WithEmptyName_ReturnsBadRequestAndLogsAudit()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(seeder => seeder.CreateProductAsync("SKU-UPDATE-INVALID", "Produit initial")).ConfigureAwait(false);
        var client = CreateClient();

        Fixture.ClearAuditLogs();
        var response = await client.PutAsJsonAsync(
            client.CreateRelativeUri("/api/products/SKU-UPDATE-INVALID"),
            new CreateProductRequest { Sku = "SKU-UPDATE-INVALID", Name = "  ", Ean = null }
        ).ConfigureAwait(false);

        await response.ShouldBeAsync(HttpStatusCode.BadRequest, "la mise à jour sans nom doit être rejetée").ConfigureAwait(false);
        var logs = Fixture.DrainAuditLogs();
        logs.Should().ContainSingle("la tentative invalide doit être auditée")
            .Which.Category.Should().Be("products.update.invalid");
    }

    [SkippableFact]
    public async Task UpdateProduct_WithNullEan_ClearsExistingEan()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        const string sku = "SKU-UPDATE-KEEP";
        const string originalEan = "45678901";
        var createResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = sku, Name = "Produit original", Ean = originalEan }
        ).ConfigureAwait(false);
        await createResponse.ShouldBeAsync(HttpStatusCode.Created, "la création initiale doit réussir").ConfigureAwait(false);

        var updateResponse = await client.PutAsJsonAsync(
            client.CreateRelativeUri($"/api/products/{Uri.EscapeDataString(sku)}"),
            new CreateProductRequest { Sku = sku, Name = "Produit renommé" }
        ).ConfigureAwait(false);

        await updateResponse.ShouldBeAsync(HttpStatusCode.OK, "la mise à jour du nom sans EAN doit réussir").ConfigureAwait(false);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Produit renommé");
        updated.Ean.Should().BeNull("l'EAN doit être effacé si nul est envoyé lors de la mise à jour");

        var fetchResponse = await client.GetAsync(client.CreateRelativeUri($"/api/products/{Uri.EscapeDataString(sku)}")).ConfigureAwait(false);
        await fetchResponse.ShouldBeAsync(HttpStatusCode.OK, "le produit doit être récupérable après mise à jour").ConfigureAwait(false);
        var fetched = await fetchResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        fetched.Should().NotBeNull();
        fetched!.Ean.Should().BeNull("l'EAN doit également être vidé côté persistance");
    }

    [SkippableFact]
    public async Task UpdateProduct_DuplicateEan_AllowsDuplication()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        const string ean1 = "12345690";
        const string ean2 = "12345691";
        var firstCreate = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-CONFLICT-1", Name = "Produit A", Ean = ean1 }
        ).ConfigureAwait(false);
        await firstCreate.ShouldBeAsync(HttpStatusCode.Created, "le premier produit doit être créé").ConfigureAwait(false);

        var secondCreate = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            new CreateProductRequest { Sku = "SKU-CONFLICT-2", Name = "Produit B", Ean = ean2 }
        ).ConfigureAwait(false);
        await secondCreate.ShouldBeAsync(HttpStatusCode.Created, "le second produit doit être créé").ConfigureAwait(false);

        var updateResponse = await client.PutAsJsonAsync(
            client.CreateRelativeUri("/api/products/SKU-CONFLICT-2"),
            new CreateProductRequest { Sku = "SKU-CONFLICT-2", Name = "Produit B", Ean = ean1 }
        ).ConfigureAwait(false);

        await updateResponse.ShouldBeAsync(HttpStatusCode.OK, "la mise à jour vers un EAN existant doit désormais réussir").ConfigureAwait(false);
        var updatedProduct = await updateResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        updatedProduct.Should().NotBeNull();
        updatedProduct!.Sku.Should().Be("SKU-CONFLICT-2");
        updatedProduct.Ean.Should().Be(ean1);

        var fetchResponse = await client.GetAsync(client.CreateRelativeUri("/api/products/SKU-CONFLICT-2")).ConfigureAwait(false);
        await fetchResponse.ShouldBeAsync(HttpStatusCode.OK, "le produit mis à jour doit être récupérable").ConfigureAwait(false);
        var fetchedProduct = await fetchResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        fetchedProduct.Should().NotBeNull();
        fetchedProduct!.Ean.Should().Be(ean1);
    }
}
