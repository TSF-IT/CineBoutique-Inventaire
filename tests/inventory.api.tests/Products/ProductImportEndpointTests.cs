using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("api-tests")]
public sealed class ProductImportEndpointTests : IntegrationTestBase
{
    public ProductImportEndpointTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }
    private static readonly string[] expected = new[] { "SKU-100", "SKU-200" };

    [SkippableFact]
    public async Task ImportProducts_WithCsvStream_ReplacesExistingRows()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.GetDefaultShopIdAsync().ConfigureAwait(false);
            await seeder.CreateProductAsync(shopId, "OLD-001", "Ancien produit", "1111111111111").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var csv = "\"barcode_rfid\";\"item\";\"descr\"\n" +
                  "\"1234567890123\";\"SKU-100\";\"Édition collector\"\n" +
                  "\"ABC-987654\";\"SKU-200\";\"Steelbook limité\"\n";

        using var content = new StringContent(csv, Encoding.Latin1, "text/csv");
        var response = await client.PostAsync($"/api/shops/{shopId}/products/import", content).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Total.Should().Be(2);
        payload.Inserted.Should().Be(2);
        payload.Updated.Should().Be(0);
        payload.WouldInsert.Should().Be(0);
        payload.ErrorCount.Should().Be(0);
        payload.DryRun.Should().BeFalse();
        payload.Skipped.Should().BeFalse();
        payload.Errors.Should().BeEmpty();
        payload.UnknownColumns.Should().BeEmpty();
        payload.ProposedGroups.Should().BeEmpty();

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT \"Sku\", \"Name\", \"Ean\", \"CodeDigits\" FROM \"Product\" WHERE \"ShopId\" = @shopId ORDER BY \"Sku\";", connection)
        {
            Parameters = { new("shopId", shopId) }
        };
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        var rows = new List<(string Sku, string Name, string? Ean, string? CodeDigits)>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        rows.Should().HaveCount(2);
        rows.Select(r => r.Sku).Should().Contain(expected);
        rows.Single(r => r.Sku == "SKU-100").Ean.Should().Be("1234567890123");
        rows.Single(r => r.Sku == "SKU-100").CodeDigits.Should().Be("1234567890123");
        rows.Single(r => r.Sku == "SKU-200").Ean.Should().Be("ABC-987654");
        rows.Single(r => r.Sku == "SKU-200").CodeDigits.Should().Be("987654");
    }

    [SkippableFact]
    public async Task ImportProducts_WithDuplicateSku_ReturnsValidationError()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.GetDefaultShopIdAsync().ConfigureAwait(false);
            await seeder.CreateProductAsync(shopId, "LEGACY", "Produit existant", "999").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var csv = "\"barcode_rfid\";\"item\";\"descr\"\n" +
                  "\"CODE-1\";\"SKU-900\";\"Produit A\"\n" +
                  "\"CODE-2\";\"SKU-900\";\"Produit B\"\n";

        using var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        var response = await client.PostAsync($"/api/shops/{shopId}/products/import", content).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Total.Should().Be(2);
        payload.Inserted.Should().Be(0);
        payload.Updated.Should().Be(0);
        payload.WouldInsert.Should().Be(0);
        payload.ErrorCount.Should().BeGreaterThan(0);
        payload.DryRun.Should().BeFalse();
        payload.Skipped.Should().BeFalse();
        payload.Errors.Should().Contain(error => error.Reason == "DUP_SKU_IN_FILE");
        payload.UnknownColumns.Should().BeEmpty();
        payload.ProposedGroups.Should().BeEmpty();

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM \"Product\" WHERE \"ShopId\" = @shopId AND \"Sku\" = 'LEGACY';", connection)
        {
            Parameters = { new("shopId", shopId) }
        };
        var remaining = (long)await command.ExecuteScalarAsync().ConfigureAwait(false);
        remaining.Should().Be(1);
    }
}
