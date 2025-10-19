using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
using Dapper;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("api-tests")]
public sealed class ProductSuggestEndpointTests : IntegrationTestBase
{
    public ProductSuggestEndpointTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }

    [SkippableFact]
    public async Task SuggestProducts_MissingQuery_ReturnsBadRequest()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        var response = await client.GetAsync("/api/products/suggest").ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task SuggestProducts_InvalidLimit_ReturnsBadRequest()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        var response = await client.GetAsync("/api/products/suggest?q=caf&limit=0").ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task SuggestProducts_WithQuery_ReturnsRankedSuggestions()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(SeedSuggestionScenarioAsync).ConfigureAwait(false);

        var client = CreateClient();
        var response = await client.GetAsync("/api/products/suggest?q=caf&limit=5").ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ProductSuggestionDto[]>().ConfigureAwait(false);
        payload.Should().NotBeNull();

        var suggestions = payload!;
        suggestions.Should().HaveCount(3);
        suggestions.Select(s => s.Sku).Should().ContainInOrder("CAF-100", "EXP-200", "SWEET-300");

        suggestions[0].Group.Should().Be("Boissons chaudes");
        suggestions[0].SubGroup.Should().Be("Cafés moulus");
        suggestions[2].Group.Should().Be("Boissons chaudes");
        suggestions[2].SubGroup.Should().Be("Cafés gourmands");
    }

    [SkippableFact]
    public async Task SuggestProducts_WithNumericQuery_UsesPrefixMatches()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            var parentId = await seeder.CreateProductGroupAsync("Rayon Cafés").ConfigureAwait(false);
            var grainsGroupId = await seeder.CreateProductGroupAsync("Grains 1kg", parentId).ConfigureAwait(false);

            await seeder.CreateProductAsync("CB-0001", "Café grains 1kg", "0001234567890").ConfigureAwait(false);
            await seeder.AssignProductToGroupAsync("CB-0001", grainsGroupId).ConfigureAwait(false);

            await seeder.CreateProductAsync("CB-0500", "Café grains 500g", "5001234567890").ConfigureAwait(false);
            await seeder.AssignProductToGroupAsync("CB-0500", grainsGroupId).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        var response = await client.GetAsync("/api/products/suggest?q=0001&limit=5").ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ProductSuggestionDto[]>().ConfigureAwait(false);
        payload.Should().NotBeNull();

        var suggestions = payload!;
        suggestions.Should().NotBeEmpty();
        suggestions.First().Sku.Should().Be("CB-0001");
        suggestions.First().SubGroup.Should().Be("Grains 1kg");
    }

    [Fact]
    public async Task Suggest_WithExactRfid_ReturnsProduct_FastPath()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        await using (var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            var gid = await connection.ExecuteScalarAsync<long>(@"
            WITH upsert AS (
              UPDATE ""ProductGroup"" SET ""Label""='Café' WHERE ""Code""='cafe' RETURNING ""Id""
            )
            INSERT INTO ""ProductGroup"" (""Code"",""Label"")
            SELECT 'cafe','Café' WHERE NOT EXISTS (SELECT 1 FROM upsert)
            RETURNING ""Id"";").ConfigureAwait(false);

            await connection.ExecuteAsync(@"
            WITH upsert AS (
              UPDATE ""Product""
              SET ""Name""='Café Grains 1kg', ""Ean""='321000000001', ""GroupId""=@gid
              WHERE ""Sku""='CB-0001' RETURNING ""Sku""
            )
            INSERT INTO ""Product"" (""Sku"",""Name"",""Ean"",""GroupId"")
            SELECT 'CB-0001','Café Grains 1kg','321000000001',@gid
            WHERE NOT EXISTS (SELECT 1 FROM upsert);", new { gid }).ConfigureAwait(false);
        }

        var client = CreateClient();

        var r1 = await client.GetAsync("/api/products/suggest?q=321000000001&limit=5").ConfigureAwait(false);
        var r2 = await client.GetAsync("/api/products/suggest?q=321 0000-00001&limit=5").ConfigureAwait(false);

        r1.EnsureSuccessStatusCode();
        r2.EnsureSuccessStatusCode();

        var j1 = await r1.Content.ReadAsStringAsync().ConfigureAwait(false);
        var j2 = await r2.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.Contains("\"sku\":\"CB-0001\"", j1);
        Assert.Contains("\"sku\":\"CB-0001\"", j2);
    }

    [SkippableFact]
    public async Task SuggestProducts_Strategies_ReturnOrderedResults()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(SeedSuggestionScenarioAsync).ConfigureAwait(false);

        var client = CreateClient();

        var skuResponse = await client.GetAsync("/api/products/suggest?q=CAF-&limit=5").ConfigureAwait(false);
        skuResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var skuPayload = await skuResponse.Content.ReadFromJsonAsync<ProductSuggestionDto[]>().ConfigureAwait(false);
        skuPayload.Should().NotBeNull();
        var skuSuggestions = skuPayload!;
        skuSuggestions.Should().NotBeEmpty();
        skuSuggestions.First().Sku.Should().Be("CAF-100");
        skuSuggestions.Select(s => s.Sku).Should().Contain("EXP-200");

        var eanResponse = await client.GetAsync("/api/products/suggest?q=2000000000002&limit=5").ConfigureAwait(false);
        eanResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var eanPayload = await eanResponse.Content.ReadFromJsonAsync<ProductSuggestionDto[]>().ConfigureAwait(false);
        eanPayload.Should().NotBeNull();
        var eanSuggestions = eanPayload!;
        eanSuggestions.Should().NotBeEmpty();
        eanSuggestions.First().Sku.Should().Be("EXP-200");

        var nameResponse = await client.GetAsync("/api/products/suggest?q=expresso&limit=5").ConfigureAwait(false);
        nameResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var namePayload = await nameResponse.Content.ReadFromJsonAsync<ProductSuggestionDto[]>().ConfigureAwait(false);
        namePayload.Should().NotBeNull();
        var nameSuggestions = namePayload!;
        nameSuggestions.Should().NotBeEmpty();
        nameSuggestions.First().Sku.Should().Be("EXP-200");

        var groupResponse = await client.GetAsync("/api/products/suggest?q=gourmands&limit=5").ConfigureAwait(false);
        groupResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var groupPayload = await groupResponse.Content.ReadFromJsonAsync<ProductSuggestionDto[]>().ConfigureAwait(false);
        groupPayload.Should().NotBeNull();
        var groupSuggestions = groupPayload!;
        groupSuggestions.Should().NotBeEmpty();
        groupSuggestions.First().Sku.Should().Be("SWEET-300");
        groupSuggestions.First().SubGroup.Should().Be("Cafés gourmands");
    }

    private static async Task SeedSuggestionScenarioAsync(TestDataSeeder seeder)
    {
        var parentId = await seeder.CreateProductGroupAsync("Boissons chaudes").ConfigureAwait(false);
        var cafesGroupId = await seeder.CreateProductGroupAsync("Cafés moulus", parentId).ConfigureAwait(false);
        var machinesGroupId = await seeder.CreateProductGroupAsync("Machines Café", parentId).ConfigureAwait(false);
        var gourmandGroupId = await seeder.CreateProductGroupAsync("Cafés gourmands", parentId).ConfigureAwait(false);

        await seeder.CreateProductAsync("CAF-100", "Café moulu fort", "1000000000001").ConfigureAwait(false);
        await seeder.AssignProductToGroupAsync("CAF-100", cafesGroupId).ConfigureAwait(false);

        await seeder.CreateProductAsync("EXP-200", "Machine expresso café", "2000000000002").ConfigureAwait(false);
        await seeder.AssignProductToGroupAsync("EXP-200", machinesGroupId).ConfigureAwait(false);

        await seeder.CreateProductAsync("SWEET-300", "Sucre roux", "3000000000003").ConfigureAwait(false);
        await seeder.AssignProductToGroupAsync("SWEET-300", gourmandGroupId).ConfigureAwait(false);
    }
}
