using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("api-tests")]
public sealed class ShopProductImportEndpointTests : IntegrationTestBase
{
    public ShopProductImportEndpointTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }

    private sealed record ImportedCountResponse(int ImportedCount);

    [SkippableFact]
    public async Task Import_ForShop_ReplacesExistingCatalog()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Import Test").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var initialCsv = "\"ean\";\"sku\";\"name\"\n" +
                         "\"1234567890123\";\"SKU-A\";\"Produit A\"\n" +
                         "\"5555555555555\";\"SKU-B\";\"Produit B\"\n";

        using var initialContent = new StringContent(initialCsv, Encoding.Latin1, "text/csv");
        var initialResponse = await client.PostAsync($"/api/shops/{shopId}/products/import", initialContent).ConfigureAwait(false);

        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialPayload = await initialResponse.Content.ReadFromJsonAsync<ImportedCountResponse>().ConfigureAwait(false);
        initialPayload.Should().NotBeNull();
        initialPayload!.ImportedCount.Should().Be(2);

        await using (var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await using var command = new NpgsqlCommand("SELECT \"Sku\" FROM \"Product\" WHERE \"ShopId\"=@ShopId ORDER BY \"Sku\";", connection)
            {
                Parameters = { new("ShopId", shopId) }
            };

            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            var skus = new List<string>();
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                skus.Add(reader.GetString(0));
            }

            skus.Should().BeEquivalentTo(new[] { "SKU-A", "SKU-B" });
        }

        var replacementCsv = "\"ean\";\"sku\";\"name\"\n" +
                              "\"9999999999999\";\"SKU-C\";\"Produit C\"\n";

        using var replacementContent = new StringContent(replacementCsv, Encoding.Latin1, "text/csv");
        var replacementResponse = await client.PostAsync($"/api/shops/{shopId}/products/import", replacementContent).ConfigureAwait(false);

        replacementResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replacementPayload = await replacementResponse.Content.ReadFromJsonAsync<ImportedCountResponse>().ConfigureAwait(false);
        replacementPayload.Should().NotBeNull();
        replacementPayload!.ImportedCount.Should().Be(1);

        await using (var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await using var command = new NpgsqlCommand("SELECT \"Sku\" FROM \"Product\" WHERE \"ShopId\"=@ShopId ORDER BY \"Sku\";", connection)
            {
                Parameters = { new("ShopId", shopId) }
            };

            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            var skus = new List<string>();
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                skus.Add(reader.GetString(0));
            }

            skus.Should().ContainSingle().Which.Should().Be("SKU-C");
        }
    }
}
