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

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            await seeder.CreateProductAsync("OLD-001", "Ancien produit", "1111111111111", Guid.Empty).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var csv = "\"barcode_rfid\";\"item\";\"descr\"\n" +
                  "\"1234567890123\";\"SKU-100\";\"Édition collector\"\n" +
                  "\"ABC-987654\";\"SKU-200\";\"Steelbook limité\"\n";

        using var content = new StringContent(csv, Encoding.Latin1, "text/csv");
        var response = await client.PostAsync("/api/products/import", content).ConfigureAwait(false);

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
        await using var command = new NpgsqlCommand("SELECT \"Sku\", \"Name\", \"Ean\", \"CodeDigits\" FROM \"Product\" ORDER BY \"Sku\";", connection);
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

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            await seeder.CreateProductAsync("LEGACY", "Produit existant", "999", Guid.Empty).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var csv = "\"barcode_rfid\";\"item\";\"descr\"\n" +
                  "\"CODE-1\";\"SKU-900\";\"Produit A\"\n" +
                  "\"CODE-2\";\"SKU-900\";\"Produit B\"\n";

        using var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        var response = await client.PostAsync("/api/products/import", content).ConfigureAwait(false);

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
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM \"Product\" WHERE \"Sku\" = 'LEGACY';", connection);
        var remaining = (long)await command.ExecuteScalarAsync().ConfigureAwait(false);
        remaining.Should().Be(1);
    }

    [SkippableFact]
    public async Task GlobalImport_Disabled_WhenMultiShopCataloguesEnabled()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var previous = Environment.GetEnvironmentVariable("TEST_MULTI_SHOP_CATALOGUES");
        Environment.SetEnvironmentVariable("TEST_MULTI_SHOP_CATALOGUES", "true");

        try
        {
            await using var factory = new InventoryApiFactory(Fixture.ConnectionString);
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Admin", "true");

            const string csv = "\"barcode_rfid\";\"item\";\"descr\"\n\"9000000000000\";\"SKU-LOCK\";\"Produit\"\n";
            using var content = new StringContent(csv, Encoding.Latin1, "text/csv");
            var response = await client.PostAsync("/api/products/import", content).ConfigureAwait(false);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
            payload.TryGetProperty("reason", out var reason).Should().BeTrue();
            reason.GetString().Should().Be("GLOBAL_IMPORT_DISABLED");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_MULTI_SHOP_CATALOGUES", previous);
        }
    }
}
