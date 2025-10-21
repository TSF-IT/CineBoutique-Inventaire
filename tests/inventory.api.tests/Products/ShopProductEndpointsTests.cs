using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("api-tests")]
public sealed class ShopProductEndpointsTests : IntegrationTestBase
{
    public ShopProductEndpointsTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }

    [SkippableFact]
    public async Task ImportProducts_ForShop_DryRunThenActual()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Import").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Shop-Admin", shopId.ToString());

        var csv = "\"barcode_rfid\";\"item\";\"descr\"\n" +
                  "\"1234567890123\";\"SHOP-SKU-100\";\"Import Shop Dry\"\n" +
                  "\"1234567890124\";\"SHOP-SKU-200\";\"Import Shop Dry 2\"\n";

        using (var dryRunContent = new StringContent(csv, Encoding.Latin1, "text/csv"))
        {
            var dryRunResponse = await client.PostAsync($"/api/shops/{shopId}/products/import?dryRun=true", dryRunContent).ConfigureAwait(false);
            await dryRunResponse.ShouldBeAsync(HttpStatusCode.OK, "le dry-run doit réussir").ConfigureAwait(false);

            var dryRunPayload = await dryRunResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
            dryRunPayload.Should().NotBeNull();
            dryRunPayload!.DryRun.Should().BeTrue();
            dryRunPayload.Inserted.Should().Be(0);
            dryRunPayload.WouldInsert.Should().Be(2);
        }

        await using (var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false))
        await using (var checkCommand = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM \"Product\" WHERE \"ShopId\" = @shop", connection))
        {
            checkCommand.Parameters.AddWithValue("shop", shopId);
            var dryCount = (long)await checkCommand.ExecuteScalarAsync().ConfigureAwait(false);
            dryCount.Should().Be(0, "le dry-run ne doit pas insérer de produits");
        }

        using (var actualContent = new StringContent(csv, Encoding.Latin1, "text/csv"))
        {
            var actualResponse = await client.PostAsync($"/api/shops/{shopId}/products/import", actualContent).ConfigureAwait(false);
            await actualResponse.ShouldBeAsync(HttpStatusCode.OK, "l'import réel doit réussir").ConfigureAwait(false);

            var payload = await actualResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
            payload.Should().NotBeNull();
            payload!.Inserted.Should().Be(2);
            payload.Skipped.Should().BeFalse();
        }

        await using (var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false))
        await using (var command = new Npgsql.NpgsqlCommand("SELECT \"Sku\", \"ShopId\" FROM \"Product\" WHERE \"ShopId\" = @shop ORDER BY \"Sku\";", connection))
        {
            command.Parameters.AddWithValue("shop", shopId);
            await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
            var results = new List<(string Sku, Guid ShopId)>();
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                results.Add((reader.GetString(0), reader.GetGuid(1)));
            }

            results.Should().HaveCount(2);
            results.All(row => row.ShopId == shopId).Should().BeTrue();
        }
    }

    [SkippableFact]
    public async Task ImportProducts_SameFile_IsSkipped()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Skip").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Shop-Admin", shopId.ToString());

        var csv = "\"barcode_rfid\";\"item\";\"descr\"\n" +
                  "\"0000000000001\";\"SKU-ONE\";\"Produit 1\"\n";

        using (var content = new StringContent(csv, Encoding.Latin1, "text/csv"))
        {
            var response = await client.PostAsync($"/api/shops/{shopId}/products/import", content).ConfigureAwait(false);
            await response.ShouldBeAsync(HttpStatusCode.OK);
        }

        using var duplicate = new StringContent(csv, Encoding.Latin1, "text/csv");
        var duplicateResponse = await client.PostAsync($"/api/shops/{shopId}/products/import", duplicate).ConfigureAwait(false);
        await duplicateResponse.ShouldBeAsync(HttpStatusCode.OK, "un import identique doit être ignoré sans erreur").ConfigureAwait(false);

        var payload = await duplicateResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Skipped.Should().BeTrue();

        await using (var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false))
        await using (var command = new Npgsql.NpgsqlCommand(
                           "SELECT COUNT(*) FROM \"ProductImport\" WHERE \"ShopId\" = @shop;",
                           connection))
        {
            command.Parameters.AddWithValue("shop", shopId);
            var processed = (long)await command.ExecuteScalarAsync().ConfigureAwait(false);
            processed.Should().Be(1, "un seul hash doit être enregistré pour la boutique");
        }
    }

    [SkippableFact]
    public async Task ImportProducts_ConcurrentRequests_Returns423()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Lock").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Shop-Admin", shopId.ToString());

        var csv = new StringBuilder();
        csv.AppendLine("\"barcode_rfid\";\"item\";\"descr\"");
        for (var i = 0; i < 200; i++)
        {
            csv.AppendLine($"\"000000000{i:000}\";\"SKU-{i:000}\";\"Produit {i:000}\"");
        }

        using var content = new StringContent(csv.ToString(), Encoding.Latin1, "text/csv");
        using var secondContent = new StringContent(csv.ToString(), Encoding.Latin1, "text/csv");

        var firstTask = client.PostAsync($"/api/shops/{shopId}/products/import", content);
        await Task.Delay(50).ConfigureAwait(false);
        var secondResponse = await client.PostAsync($"/api/shops/{shopId}/products/import", secondContent).ConfigureAwait(false);
        var firstResponse = await firstTask.ConfigureAwait(false);

        await firstResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);
        await secondResponse.ShouldBeAsync(HttpStatusCode.Locked).ConfigureAwait(false);
        var reason = await secondResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        reason.GetProperty("reason").GetString().Should().Be("import_in_progress");
    }

    [SkippableFact]
    public async Task ListProducts_ReturnsPaginatedResults()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Liste").ConfigureAwait(false);
            await seeder.CreateProductAsync("SKU-A", "Produit A", shopId: shopId).ConfigureAwait(false);
            await seeder.CreateProductAsync("SKU-B", "Produit B", "12345678", shopId).ConfigureAwait(false);
            await seeder.CreateProductAsync("SKU-C", "Produit C", shopId: shopId).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Shop-Admin", shopId.ToString());

        var response = await client.GetAsync($"/api/shops/{shopId}/products?page=1&pageSize=2&sortBy=name&sortDir=desc&q=Produit").ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<ShopProductListResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Page.Should().Be(1);
        payload.PageSize.Should().Be(2);
        payload.Total.Should().Be(3);
        payload.TotalPages.Should().Be(2);
        payload.SortBy.Should().Be("name");
        payload.SortDir.Should().Be("desc");
        payload.Q.Should().Be("Produit");
        payload.Items.Should().HaveCount(2);
        payload.Items.First().Name.Should().Be("Produit C");
    }

    [SkippableFact]
    public async Task ListProducts_FilterMatchesSkuEanAndCodeDigits()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        Guid rfidProductId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Recherche").ConfigureAwait(false);
            await seeder.CreateProductAsync("SKU-BASE", "Produit générique", shopId: shopId).ConfigureAwait(false);
            await seeder.CreateProductAsync("XYZ-REF", "Produit ciblé", shopId: shopId).ConfigureAwait(false);
            await seeder.CreateProductAsync("SKU-EAN", "Produit code", "9900001112223", shopId).ConfigureAwait(false);
            rfidProductId = await seeder.CreateProductAsync("SKU-RFID", "Produit RFID", shopId: shopId).ConfigureAwait(false);
        }).ConfigureAwait(false);

        await using (var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false))
        await using (var command = new Npgsql.NpgsqlCommand(
                               "UPDATE \"Product\" SET \"CodeDigits\" = @codeDigits WHERE \"Id\" = @id;",
                               connection))
        {
            command.Parameters.AddWithValue("codeDigits", "000123");
            command.Parameters.AddWithValue("id", rfidProductId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Shop-Admin", shopId.ToString());

        var skuResponse = await client.GetAsync($"/api/shops/{shopId}/products?q=XYZ-REF").ConfigureAwait(false);
        await skuResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);
        var skuPayload = await skuResponse.Content.ReadFromJsonAsync<ShopProductListResponse>().ConfigureAwait(false);
        skuPayload.Should().NotBeNull();
        skuPayload!.Total.Should().Be(1);
        skuPayload.Items.Should().ContainSingle();
        skuPayload.Items.Single().Sku.Should().Be("XYZ-REF");

        var eanResponse = await client.GetAsync($"/api/shops/{shopId}/products?q=9900001112223").ConfigureAwait(false);
        await eanResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);
        var eanPayload = await eanResponse.Content.ReadFromJsonAsync<ShopProductListResponse>().ConfigureAwait(false);
        eanPayload.Should().NotBeNull();
        eanPayload!.Total.Should().Be(1);
        eanPayload.Items.Should().ContainSingle();
        eanPayload.Items.Single().Sku.Should().Be("SKU-EAN");

        var digitsResponse = await client.GetAsync($"/api/shops/{shopId}/products?q=000123").ConfigureAwait(false);
        await digitsResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);
        var digitsPayload = await digitsResponse.Content.ReadFromJsonAsync<ShopProductListResponse>().ConfigureAwait(false);
        digitsPayload.Should().NotBeNull();
        digitsPayload!.Total.Should().Be(1);
        digitsPayload.Items.Should().ContainSingle();
        digitsPayload.Items.Single().Sku.Should().Be("SKU-RFID");
    }

    [SkippableFact]
    public async Task CountEndpoint_ReturnsCatalogInfo()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Count").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Shop-Admin", shopId.ToString());

        var initialResponse = await client.GetAsync($"/api/shops/{shopId}/products/count").ConfigureAwait(false);
        await initialResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);
        var initialPayload = await initialResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        initialPayload.GetProperty("count").GetInt64().Should().Be(0);
        initialPayload.GetProperty("hasCatalog").GetBoolean().Should().BeFalse();

        var csv = "\"barcode_rfid\";\"item\";\"descr\"\n" +
                  "\"0090000000000\";\"COUNT-SKU\";\"Produit Count\"\n";
        using var content = new StringContent(csv, Encoding.Latin1, "text/csv");
        var importResponse = await client.PostAsync($"/api/shops/{shopId}/products/import", content).ConfigureAwait(false);
        await importResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);

        var finalResponse = await client.GetAsync($"/api/shops/{shopId}/products/count").ConfigureAwait(false);
        await finalResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);
        var finalPayload = await finalResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        finalPayload.GetProperty("count").GetInt64().Should().Be(1);
        finalPayload.GetProperty("hasCatalog").GetBoolean().Should().BeTrue();
    }
}
