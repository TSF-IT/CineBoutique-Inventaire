using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
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

        await Fixture.ResetAndSeedAsync(async seeder =>
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
        }).ConfigureAwait(false);

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
}
