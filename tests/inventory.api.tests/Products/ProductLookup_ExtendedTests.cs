using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("api-tests")]
public sealed class ProductLookup_ExtendedTests : IntegrationTestBase
{
    public ProductLookup_ExtendedTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }

    [SkippableFact]
    public async Task GetProduct_ByExactSku_ReturnsExpectedItem()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var request = new CreateProductRequest
        {
            Sku = "LKP-SKU-001",
            Name = "Saga UHD",
            Ean = "1234567890123"
        };

        var createResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/products"),
            request).ConfigureAwait(false);
        await createResponse.ShouldBeAsync(HttpStatusCode.Created, "la création prépare la recherche par SKU").ConfigureAwait(false);

        var getResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/products/{Uri.EscapeDataString(request.Sku!)}")).ConfigureAwait(false);
        await getResponse.ShouldBeAsync(HttpStatusCode.OK, "le SKU exact doit matcher en priorité").ConfigureAwait(false);

        var product = await getResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        product.Should().NotBeNull();
        product!.Sku.Should().Be(request.Sku);
        product.Name.Should().Be(request.Name);
        product.Ean.Should().Be(request.Ean);
    }

    [SkippableFact]
    public async Task GetProduct_ByRawCodeWithWhitespace_Succeeds()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        await InsertProductAsync("LKP-RAW-001", "Code brut avec espace", "33906 56", "3390656").ConfigureAwait(false);

        var client = CreateClient();
        var getResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/products/{Uri.EscapeDataString("33906 56")}")).ConfigureAwait(false);
        await getResponse.ShouldBeAsync(HttpStatusCode.OK, "le code brut doit être résolu après l'échec SKU").ConfigureAwait(false);

        var product = await getResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        product.Should().NotBeNull();
        product!.Sku.Should().Be("LKP-RAW-001");
        product.Name.Should().Be("Code brut avec espace");
        product.Ean.Should().Be("33906 56");
    }

    [SkippableFact]
    public async Task GetProduct_ByDigitsFromAlphaSuffix_Succeeds()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        // L'EAN est limité à 13 caractères en base : on simule un suffixe alpha en remplaçant le dernier chiffre.
        const string rawCodeWithSuffix = "355719131003S"; // 12 chiffres + suffixe alpha
        const string digitsOnly = "355719131003";
        await InsertProductAsync("LKP-DGT-001", "Code suffixé alpha", rawCodeWithSuffix, digitsOnly).ConfigureAwait(false);

        var client = CreateClient();
        var getResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/products/{digitsOnly}")).ConfigureAwait(false);
        await getResponse.ShouldBeAsync(HttpStatusCode.OK, "les chiffres extraits doivent permettre de retrouver le produit").ConfigureAwait(false);

        var product = await getResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        product.Should().NotBeNull();
        product!.Sku.Should().Be("LKP-DGT-001");
        product.Name.Should().Be("Code suffixé alpha");
        product.Ean.Should().Be(rawCodeWithSuffix);
    }
    private static readonly string[] expected = ["LKP-AMB-001", "LKP-AMB-002"];

    [SkippableFact]
    public async Task GetProduct_DigitsConflict_ReturnsAmbiguityPayload()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        await InsertProductAsync("LKP-AMB-001", "EAN principal", "5905954595389", "5905954595389").ConfigureAwait(false);
        await InsertProductAsync("LKP-AMB-002", "EAN doublon espaces", "5905954595389 ", "5905954595389").ConfigureAwait(false);

        var client = CreateClient();
        var getResponse = await client.GetAsync(
            client.CreateRelativeUri("/api/products/5905954595389")).ConfigureAwait(false);
        getResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, "les chiffres partagés doivent produire un 409");

        var conflict = await getResponse.Content.ReadFromJsonAsync<ProductLookupConflictResponse>().ConfigureAwait(false);
        conflict.Should().NotBeNull();
        conflict!.Ambiguous.Should().BeTrue();
        conflict.Code.Should().Be("5905954595389");
        conflict.Digits.Should().Be("5905954595389");
        conflict.Matches.Should().HaveCountGreaterOrEqualTo(2);
        conflict.Matches.Select(m => m.Sku).Should().Contain(expected);
    }

    [SkippableFact]
    public async Task GetProduct_UnknownCode_Returns404()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        var getResponse = await client.GetAsync(
            client.CreateRelativeUri("/api/products/UNKNOWN-CODE-999")).ConfigureAwait(false);
        await getResponse.ShouldBeAsync(HttpStatusCode.NotFound, "un code inconnu doit remonter 404").ConfigureAwait(false);
    }

    private async Task InsertProductAsync(string sku, string name, string? ean, string? codeDigits)
    {
        var id = Guid.NewGuid();
        var shopId = await Fixture.Seeder.GetOrCreateAnyShopIdAsync().ConfigureAwait(false);
        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);

        const string sql = """
INSERT INTO "Product" ("Id", "ShopId", "Sku", "Name", "Ean", "CodeDigits", "CreatedAtUtc")
VALUES (@Id, @ShopId, @Sku, @Name, @Ean, @CodeDigits, @CreatedAtUtc);
""";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            Parameters =
            {
                new("Id", id),
                new("ShopId", shopId),
                new("Sku", sku),
                new("Name", name),
                new("Ean", (object?)ean ?? DBNull.Value),
                new("CodeDigits", (object?)codeDigits ?? DBNull.Value),
                new("CreatedAtUtc", DateTimeOffset.UtcNow)
            }
        };

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
