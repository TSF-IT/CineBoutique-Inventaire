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
using Microsoft.AspNetCore.Mvc;
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
        payload.WouldUpdate.Should().Be(0);
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
    public async Task ImportProducts_WithRfidColumn_MapsToEan()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.GetDefaultShopIdAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        const string csv = "\"rfid\";\"item\";\"descr\"\n" +
                           "\"pmi_ac_hzfan\";\"SKU-RFID\";\"Anaphore - Sparadrap Transpore microperforé - Blanc\"\n";

        using var content = new StringContent(csv, Encoding.Latin1, "text/csv");
        var response = await client.PostAsync($"/api/shops/{shopId}/products/import", content).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"Name\", \"Ean\" FROM \"Product\" WHERE \"ShopId\" = @shopId AND \"Sku\" = 'SKU-RFID';",
            connection)
        {
            Parameters = { new("shopId", shopId) }
        };

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var found = await reader.ReadAsync().ConfigureAwait(false);
        found.Should().BeTrue("le produit importé doit être présent dans le catalogue");
        reader.GetString(0).Should().Be("Anaphore - Sparadrap Transpore microperforé - Blanc");
        reader.GetString(1).Should().Be("pmi_ac_hzfan");
    }

    [SkippableFact]
    public async Task ImportProducts_WithDuplicateSku_ImportsAndReportsDuplicate()
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
        var response = await client.PostAsync($"/api/shops/{shopId}/products/import?mode=merge", content).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Total.Should().Be(2);
        payload.ErrorCount.Should().Be(0);
        payload.DryRun.Should().BeFalse();
        payload.Skipped.Should().BeFalse();
        payload.WouldInsert.Should().Be(0);
        payload.WouldUpdate.Should().Be(0);
        payload.Errors.Should().BeEmpty();
        payload.UnknownColumns.Should().BeEmpty();
        payload.ProposedGroups.Should().BeEmpty();
        payload.SkippedLines.Should().BeEmpty();
        payload.Duplicates.Should().NotBeNull();
        payload.Duplicates.Skus.Should().ContainSingle();
        payload.Duplicates.Skus[0].Value.Should().Be("SKU-900");
        payload.Duplicates.Skus[0].Lines.Should().Contain(new[] { 2, 3 });
        payload.Duplicates.Eans.Should().BeEmpty();

    }

    [SkippableFact]
    public async Task ImportProducts_AllowsRowsWithoutEan()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.GetDefaultShopIdAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var csv = "\"barcode_rfid\";\"item\";\"descr\"\n" +
                  "\"\";\"SKU-100\";\"Produit Sans Code\"\n" +
                  "\"3216549870123\";\"SKU-200\";\"Produit Avec Code\"\n";

        using var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        var response = await client.PostAsync($"/api/shops/{shopId}/products/import", content).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Total.Should().Be(2);
        payload.Inserted.Should().Be(2);
        payload.Updated.Should().Be(0);
        payload.ErrorCount.Should().Be(0);
        payload.WouldInsert.Should().Be(0);
        payload.WouldUpdate.Should().Be(0);
        payload.Skipped.Should().BeFalse();
        payload.SkippedLines.Should().BeEmpty();
        payload.Duplicates.Skus.Should().BeEmpty();
        payload.Duplicates.Eans.Should().BeEmpty();

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var countCommand = new NpgsqlCommand("SELECT COUNT(*) FROM \"Product\" WHERE \"ShopId\" = @shopId;", connection)
        {
            Parameters = { new("shopId", shopId) }
        };
        var totalProducts = (long)await countCommand.ExecuteScalarAsync().ConfigureAwait(false);
        totalProducts.Should().Be(2);
    }

    [SkippableFact]
    public async Task ImportProducts_ReplaceBlockedByCounts_ReturnsConflictProblem()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid shopId = Guid.Empty;
        Guid locationId = Guid.Empty;
        Guid productId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.GetDefaultShopIdAsync().ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "ZBLO", "Zone bloquée").ConfigureAwait(false);
            productId = await seeder.CreateProductAsync(shopId, "SKU-LOCK", "Produit bloqué", "5555555555555").ConfigureAwait(false);
        }).ConfigureAwait(false);

        await InsertCountingRunWithLineAsync(locationId, productId).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var csv = "\"barcode\";\"item\";\"descr\"\n" +
                  "\"1111111111111\";\"SKU-NEW\";\"Nouveau produit\"\n";

        using var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        var response = await client.PostAsync($"/api/shops/{shopId}/products/import", content).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>().ConfigureAwait(false);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Suppression impossible");
        problem.Status.Should().Be((int)HttpStatusCode.Conflict);
        problem.Detail.Should().Contain("comptages");

        problem.Extensions.Should().ContainKey("blocked");
        var blockedJson = problem.Extensions["blocked"] as JsonElement?;
        blockedJson.HasValue.Should().BeTrue();

        var blockedElement = blockedJson!.Value;
        blockedElement.TryGetProperty("sampleProductIds", out var sampleIdsElement).Should().BeTrue();
        var sampleIds = sampleIdsElement.EnumerateArray()
            .Select(item => Guid.Parse(item.GetString() ?? Guid.Empty.ToString()))
            .ToArray();
        sampleIds.Should().Contain(productId);

        if (blockedElement.TryGetProperty("locationId", out var locationElement) &&
            locationElement.ValueKind == JsonValueKind.String)
        {
            Guid.Parse(locationElement.GetString()!).Should().Be(locationId);
        }
    }

    private async Task InsertCountingRunWithLineAsync(Guid locationId, Guid productId)
    {
        var sessionId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        await using (var sessionCommand = new NpgsqlCommand(
                   "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@id, @name, @startedAt);",
                   connection,
                   transaction))
        {
            sessionCommand.Parameters.AddWithValue("id", sessionId);
            sessionCommand.Parameters.AddWithValue("name", "Session blocage");
            sessionCommand.Parameters.AddWithValue("startedAt", DateTimeOffset.UtcNow);
            await sessionCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using (var runCommand = new NpgsqlCommand(
                   "INSERT INTO \"CountingRun\" (\"Id\", \"InventorySessionId\", \"LocationId\", \"CountType\", \"StartedAtUtc\", \"CompletedAtUtc\", \"OperatorDisplayName\") VALUES (@id, @sessionId, @locationId, @countType, @startedAt, NULL, @operator);",
                   connection,
                   transaction))
        {
            runCommand.Parameters.AddWithValue("id", runId);
            runCommand.Parameters.AddWithValue("sessionId", sessionId);
            runCommand.Parameters.AddWithValue("locationId", locationId);
            runCommand.Parameters.AddWithValue("countType", (short)1);
            runCommand.Parameters.AddWithValue("startedAt", DateTimeOffset.UtcNow);
            runCommand.Parameters.AddWithValue("operator", "Tests");
            await runCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using (var lineCommand = new NpgsqlCommand(
                   "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@id, @runId, @productId, @quantity, @countedAt);",
                   connection,
                   transaction))
        {
            lineCommand.Parameters.AddWithValue("id", lineId);
            lineCommand.Parameters.AddWithValue("runId", runId);
            lineCommand.Parameters.AddWithValue("productId", productId);
            lineCommand.Parameters.AddWithValue("quantity", 1m);
            lineCommand.Parameters.AddWithValue("countedAt", DateTimeOffset.UtcNow);
            await lineCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }
}
