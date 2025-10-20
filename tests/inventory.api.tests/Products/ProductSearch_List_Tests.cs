using System;
using System.Collections.Generic;
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
public sealed class ProductSearch_List_Tests : IntegrationTestBase
{
    public ProductSearch_List_Tests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }

    [SkippableFact]
    public async Task Search_BySkuPriority_ListsSkuMatchFirst()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        await InsertProductAsync("SRCH-SKU-001", "Produit avec SKU", "9000000000000", "9000000000000").ConfigureAwait(false);
        await InsertProductAsync("SRCH-RAW-001", "Produit code brut", "SRCH-SKU-001", null).ConfigureAwait(false);

        var client = CreateClient();
        var response = await client.GetAsync(client.CreateRelativeUri("/api/products/search?code=SRCH-SKU-001"))
            .ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "la recherche doit réussir").ConfigureAwait(false);

        var items = await response.Content.ReadFromJsonAsync<ProductSearchItemDto[]>().ConfigureAwait(false);
        items.Should().NotBeNull();
        items!.Should().HaveCountGreaterOrEqualTo(2, "un match SKU et un match code brut sont attendus");
        items[0].Sku.Should().Be("SRCH-SKU-001", "le SKU exact doit être prioritaire");
        items[1].Sku.Should().Be("SRCH-RAW-001", "le code brut suit après le SKU");
    }

    [SkippableFact]
    public async Task Search_ByRawCodeWithSpaces_ReturnsMatch()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        const string rawCodeWithSpaces = "33906 56";
        await InsertProductAsync("SRCH-RAW-SPC", "Code brut avec espaces", rawCodeWithSpaces, "3390656", rawCodeWithSpaces)
            .ConfigureAwait(false);

        var client = CreateClient();
        var response = await client.GetAsync(
                client.CreateRelativeUri($"/api/products/search?code={Uri.EscapeDataString(rawCodeWithSpaces)}"))
            .ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "la recherche doit réussir").ConfigureAwait(false);

        var items = await response.Content.ReadFromJsonAsync<ProductSearchItemDto[]>().ConfigureAwait(false);
        items.Should().NotBeNull();
        items!.Select(i => i.Sku).Should().Contain("SRCH-RAW-SPC", "le produit doit être renvoyé");
    }

    [SkippableFact]
    public async Task Search_ByDigitsFromSuffixedCode_ReturnsMatch()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        const string rawCodeWithSuffix = "3557191310038X";
        const string digitsOnly = "3557191310038";
        await InsertProductAsync("SRCH-DGT-001", "Code suffixé", digitsOnly, digitsOnly, rawCodeWithSuffix)
            .ConfigureAwait(false);

        var client = CreateClient();
        var response = await client.GetAsync(client.CreateRelativeUri($"/api/products/search?code={digitsOnly}"))
            .ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "la recherche doit réussir").ConfigureAwait(false);

        var items = await response.Content.ReadFromJsonAsync<ProductSearchItemDto[]>().ConfigureAwait(false);
        items.Should().NotBeNull();
        items!.Select(i => i.Sku).Should().Contain("SRCH-DGT-001", "le produit doit être trouvé via les chiffres extraits");
    }

    [SkippableFact]
    public async Task Search_WithDigitsAmbiguity_ListsDigitsMatchesAfterExactOnes()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        await InsertProductAsync("5905954595389", "Priorité SKU", "1111111111111", "1111111111111").ConfigureAwait(false);
        await InsertProductAsync("AMB-RAW-001", "Correspondance brute", "5905954595389", "5905954595389", "5905954595389")
            .ConfigureAwait(false);
        await InsertProductAsync("AMB-DGT-001", "Ambigu digits A", "5905954595389", "5905954595389", "5905954595389 ")
            .ConfigureAwait(false);
        await InsertProductAsync("AMB-DGT-002", "Ambigu digits B", "5905954595389", "5905954595389", "SKU-5905954595389")
            .ConfigureAwait(false);

        var client = CreateClient();
        var response = await client.GetAsync(client.CreateRelativeUri("/api/products/search?code=5905954595389"))
            .ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "la recherche doit réussir").ConfigureAwait(false);

        var items = await response.Content.ReadFromJsonAsync<ProductSearchItemDto[]>().ConfigureAwait(false);
        items.Should().NotBeNull();
        items!.Should().HaveCountGreaterOrEqualTo(4, "un SKU, un code brut et deux matches digits sont attendus");
        items![0].Sku.Should().Be("5905954595389", "le SKU exact est prioritaire");

        var subsequentSkus = items.Skip(1).Select(i => i.Sku).ToArray();
        subsequentSkus.Should().Contain("AMB-RAW-001", "le code brut doit être renvoyé dans les résultats");
        subsequentSkus.Should().Contain(expected,
            "les correspondances digits sont listées après la correspondance exacte");
    }

    [SkippableFact]
    public async Task Search_WithLimit_TruncatesResults()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        await InsertProductAsync("LIM-SKU-001", "Produit prioritaire", "2222222222222", "2222222222222").ConfigureAwait(false);
        await InsertProductAsync("LIM-RAW-001", "Produit secondaire", "LIM-SKU-001", null).ConfigureAwait(false);

        var client = CreateClient();
        var response = await client.GetAsync(client.CreateRelativeUri("/api/products/search?code=LIM-SKU-001&limit=1"))
            .ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "la recherche doit réussir").ConfigureAwait(false);

        var items = await response.Content.ReadFromJsonAsync<ProductSearchItemDto[]>().ConfigureAwait(false);
        items.Should().NotBeNull();
        items!.Should().HaveCount(1, "la limite doit tronquer les résultats");
        items[0].Sku.Should().Be("LIM-SKU-001", "le SKU exact est conservé lors du troncature");
    }

    private static bool? _supportsRawCodeColumn;
    private static readonly string[] expected = new[] { "AMB-DGT-001", "AMB-DGT-002" };

    private async Task InsertProductAsync(string sku, string name, string? ean, string? codeDigits, string? rawCode = null)
    {
        var id = Guid.NewGuid();
        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);

        var columns = new List<string>
        {
            "\"Id\"",
            "\"Sku\"",
            "\"Name\"",
            "\"Ean\"",
            "\"CodeDigits\"",
            "\"CreatedAtUtc\""
        };

        var values = new List<string>
        {
            "@Id",
            "@Sku",
            "@Name",
            "@Ean",
            "@CodeDigits",
            "@CreatedAtUtc"
        };

        var supportsRawCode = await SupportsRawCodeColumnAsync(connection).ConfigureAwait(false);
        if (supportsRawCode)
        {
            columns.Insert(4, "\"Code\"");
            values.Insert(4, "@Code");
        }

        var sql = $"INSERT INTO \"Product\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)});";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            Parameters =
            {
                new("Id", id),
                new("Sku", sku),
                new("Name", name),
                new("Ean", (object?)ean ?? DBNull.Value),
                new("CodeDigits", (object?)codeDigits ?? DBNull.Value),
                new("CreatedAtUtc", DateTimeOffset.UtcNow)
            }
        };

        if (supportsRawCode)
        {
            command.Parameters.AddWithValue("Code", (object?)rawCode ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<bool> SupportsRawCodeColumnAsync(NpgsqlConnection connection)
    {
        if (_supportsRawCodeColumn.HasValue)
        {
            return _supportsRawCodeColumn.Value;
        }

        const string sql = @"SELECT EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE LOWER(table_name) = LOWER(@Table)
      AND LOWER(column_name) = LOWER(@Column)
);";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            Parameters =
            {
                new("Table", "Product"),
                new("Column", "Code")
            }
        };

        var hasColumn = (bool)await command.ExecuteScalarAsync().ConfigureAwait(false);
        _supportsRawCodeColumn = hasColumn;
        return hasColumn;
    }
}
