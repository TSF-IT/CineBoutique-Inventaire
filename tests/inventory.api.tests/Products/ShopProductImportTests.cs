using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("db")]
public sealed class ShopProductImportTests : IntegrationTestBase
{
    private static readonly string[] InitialCsvLines =
    {
        "barcode_rfid;item;descr",
        "24719;A-KF63030;Prod X",
        "33906 56;A-GOBBIO16;Gob bio"
    };

    public ShopProductImportTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }

    private async Task<Guid> ResetAndSeedWithShopAsync(Func<TestDataSeeder, Guid, Task>? plan = null)
    {
        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.GetDefaultShopIdAsync().ConfigureAwait(false);
            if (plan is not null)
            {
                await plan(seeder, shopId).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        return shopId;
    }

    private static Task<HttpResponseMessage> PostImportAsync(
        HttpClient client,
        Guid shopId,
        HttpContent content,
        bool dryRun = false,
        string? mode = null)
    {
        var query = new List<string>();
        if (dryRun)
        {
            query.Add("dryRun=true");
        }

        if (!string.IsNullOrWhiteSpace(mode))
        {
            query.Add($"mode={Uri.EscapeDataString(mode)}");
        }

        var queryString = query.Count > 0 ? $"?{string.Join("&", query)}" : string.Empty;
        var uri = $"/api/shops/{shopId}/products/import{queryString}";
        return client.PostAsync(uri, content);
    }

    [SkippableFact]
    public async Task ShopImport_ReplacesOnlyCurrentShopCatalogue()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid secondaryShopId = Guid.Empty;
        var shopId = await ResetAndSeedWithShopAsync(async (seeder, defaultShopId) =>
        {
            secondaryShopId = await seeder.CreateShopAsync("Test Boutique Secondaire").ConfigureAwait(false);
            await seeder.CreateProductAsync(secondaryShopId, "OTHER-001", "Produit Autre").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using (var initialContent = CreateCsvContent(InitialCsvLines))
        {
            var initialResponse = await PostImportAsync(client, shopId, initialContent).ConfigureAwait(false);
            await initialResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);
        }

        var reimportLines = new[]
        {
            "barcode_rfid;item;descr",
            "NEWCODE1;A-NEW1;Nouveau 1",
            "NEWCODE2;A-NEW2;Nouveau 2"
        };

        using var reimportContent = CreateCsvContent(reimportLines);
        var reimportResponse = await PostImportAsync(client, shopId, reimportContent).ConfigureAwait(false);
        await reimportResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);

        var payload = await reimportResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Inserted.Should().Be(2);
        payload.Updated.Should().Be(0);

        await using var verifyConnection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        const string sql = "SELECT \"ShopId\", \"Sku\" FROM \"Product\" ORDER BY \"ShopId\", \"Sku\";";
        await using var command = new NpgsqlCommand(sql, verifyConnection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        var perShop = reader.Cast<IDataRecord>()
            .Select(record => (ShopId: record.GetGuid(0), Sku: record.GetString(1)))
            .ToArray();

        perShop.Should().Contain((secondaryShopId, "OTHER-001"));
        perShop.Where(entry => entry.ShopId == shopId).Select(entry => entry.Sku)
            .Should().BeEquivalentTo(new[] { "A-NEW1", "A-NEW2" });
    }

    [SkippableFact]
    public async Task ShopImport_AllowsDuplicateSkusAcrossShops()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid secondaryShopId = Guid.Empty;
        var primaryShopId = await ResetAndSeedWithShopAsync(async (seeder, defaultShopId) =>
        {
            secondaryShopId = await seeder.CreateShopAsync("Boutique duplicats").ConfigureAwait(false);
            await seeder.CreateProductAsync(defaultShopId, "SHARED-001", "Produit principal").ConfigureAwait(false);
            await seeder.CreateProductAsync(secondaryShopId, "SECOND-OLD", "Ancien produit secondaire").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var importLines = new[]
        {
            "barcode_rfid;item;descr",
            "456789;SHARED-001;Produit secondaire"
        };

        using var importContent = CreateCsvContent(importLines);
        var response = await PostImportAsync(client, secondaryShopId, importContent).ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Inserted.Should().Be(1);
        payload.Updated.Should().Be(0);
        payload.ErrorCount.Should().Be(0);

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        const string sql = "SELECT \"ShopId\", \"Sku\" FROM \"Product\" WHERE \"Sku\" = @sku ORDER BY \"ShopId\";";
        await using var command = new NpgsqlCommand(sql, connection)
        {
            Parameters = { new("sku", "SHARED-001") }
        };

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var perShop = new List<(Guid ShopId, string Sku)>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            perShop.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        perShop.Should().HaveCount(2);
        perShop.Should().Contain((primaryShopId, "SHARED-001"));
        perShop.Should().Contain((secondaryShopId, "SHARED-001"));
    }

    [SkippableFact]
    public async Task ShopImport_DryRun_DoesNotPersistRows()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        var shopId = await ResetAndSeedWithShopAsync().ConfigureAwait(false);
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using (var dryRunContent = CreateCsvContent(InitialCsvLines))
        {
            var dryRunResponse = await PostImportAsync(client, shopId, dryRunContent, dryRun: true).ConfigureAwait(false);
            await dryRunResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);

            var dryRunPayload = await dryRunResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
            dryRunPayload.Should().NotBeNull();
            dryRunPayload!.DryRun.Should().BeTrue();
            dryRunPayload.Inserted.Should().Be(0);
            // Le CSV de test contient deux lignes de données : la prévisualisation doit refléter ces deux insertions.
            dryRunPayload.WouldInsert.Should().Be(2);
        }

        await using var verifyConnection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var verifyCommand = new NpgsqlCommand("SELECT COUNT(*) FROM \"Product\" WHERE \"ShopId\" = @shopId;", verifyConnection)
        {
            Parameters = { new("shopId", shopId) }
        };

        var count = (long)await verifyCommand.ExecuteScalarAsync().ConfigureAwait(false);
        count.Should().Be(0);
    }

    [SkippableFact]
    public async Task ShopImport_DryRun_WithUnknownColumns_ReturnsMetadata()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        var shopId = await ResetAndSeedWithShopAsync().ConfigureAwait(false);
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var lines = new[]
        {
            "sku;descr;custom_meta",
            "SKU-1;Produit test;Valeur"
        };

        using var content = CreateCsvContent(lines);
        var response = await PostImportAsync(client, shopId, content, dryRun: true).ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.DryRun.Should().BeTrue();
        payload.WouldInsert.Should().Be(1);
        payload.ErrorCount.Should().Be(0);
        payload.UnknownColumns.Should().ContainSingle().Which.Should().Be("custom_meta");

        await using var verifyConnection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var verifyCommand = new NpgsqlCommand("SELECT COUNT(*) FROM \"Product\" WHERE \"ShopId\" = @shopId;", verifyConnection)
        {
            Parameters = { new("shopId", shopId) }
        };

        var count = (long)await verifyCommand.ExecuteScalarAsync().ConfigureAwait(false);
        count.Should().Be(0);
    }

    [SkippableFact]
    public async Task ShopImport_ImportOnSecondaryShop_DoesNotAffectPrimaryCatalogue()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid secondaryShopId = Guid.Empty;
        var primaryShopId = await ResetAndSeedWithShopAsync(async (seeder, defaultShopId) =>
        {
            secondaryShopId = await seeder.CreateShopAsync("Boutique secondaire").ConfigureAwait(false);
            await seeder.CreateProductAsync(defaultShopId, "PRIMARY-001", "Produit principal").ConfigureAwait(false);
            await seeder.CreateProductAsync(secondaryShopId, "SECOND-001", "Produit secondaire").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var importLines = new[]
        {
            "barcode_rfid;item;descr",
            "1111111;SECOND-NEW;Produit secondaire nouveau"
        };

        using var content = CreateCsvContent(importLines);
        var response = await PostImportAsync(client, secondaryShopId, content).ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Inserted.Should().Be(1);
        payload.Updated.Should().Be(0);

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        const string sql = "SELECT \"ShopId\", \"Sku\" FROM \"Product\" ORDER BY \"ShopId\", \"Sku\";";
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        var perShop = new List<(Guid ShopId, string Sku)>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            perShop.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        perShop.Should().Contain((primaryShopId, "PRIMARY-001"));
        perShop.Should().Contain((secondaryShopId, "SECOND-NEW"));
        perShop.Should().NotContain((secondaryShopId, "SECOND-001"));
    }

    [SkippableFact]
    public async Task ShopImport_MergeMode_AppendsWithoutDeleting()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        var shopId = await ResetAndSeedWithShopAsync().ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using (var initialContent = CreateCsvContent(InitialCsvLines))
        {
            var initialResponse = await PostImportAsync(client, shopId, initialContent).ConfigureAwait(false);
            await initialResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);
        }

        var mergeLines = new[]
        {
            "barcode_rfid;item;descr",
            "987654;A-APPEND1;Produit ajouté",
        };

        using var mergeContent = CreateCsvContent(mergeLines);
        var mergeResponse = await PostImportAsync(client, shopId, mergeContent, dryRun: false, mode: "merge")
            .ConfigureAwait(false);
        await mergeResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);

        var payload = await mergeResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Inserted.Should().Be(1);
        payload.Updated.Should().Be(0);

        await using var verifyConnection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        const string sql = "SELECT \"Sku\" FROM \"Product\" WHERE \"ShopId\" = @shopId ORDER BY \"Sku\";";
        await using var command = new NpgsqlCommand(sql, verifyConnection)
        {
            Parameters = { new("shopId", shopId) }
        };

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var skus = new List<string>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            skus.Add(reader.GetString(0));
        }

        skus.Should().BeEquivalentTo(new[] { "A-APPEND1", "A-GOBBIO16", "A-KF63030" });
    }

    [SkippableFact]
    public async Task ShopImport_RejectsReplaceModeWhenOpenRuns()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        Guid locationId = Guid.Empty;
        Guid operatorId = Guid.Empty;

        var shopId = await ResetAndSeedWithShopAsync(async (seeder, defaultShopId) =>
        {
            locationId = await seeder.CreateLocationAsync(defaultShopId, "CSV-ZONE", "Zone Import").ConfigureAwait(false);
            operatorId = await seeder.CreateShopUserAsync(defaultShopId, "importer", "Importeur").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var adminClient = CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Admin", "true");

        using (var initialContent = CreateCsvContent(InitialCsvLines))
        {
            var initialResponse = await PostImportAsync(adminClient, shopId, initialContent).ConfigureAwait(false);
            await initialResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);
        }

        var operatorClient = CreateClient();
        var startResponse = await operatorClient.PostAsJsonAsync(
                operatorClient.CreateRelativeUri($"/api/inventories/{locationId}/start"),
                new StartRunRequest(shopId, operatorId, 1))
            .ConfigureAwait(false);
        await startResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);

        var replaceLines = new[]
        {
            "barcode_rfid;item;descr",
            "222222;A-NEW-REPLACE;Produit remplaçant",
        };

        using var replaceContent = CreateCsvContent(replaceLines);
        var replaceResponse = await PostImportAsync(adminClient, shopId, replaceContent).ConfigureAwait(false);
        await replaceResponse.ShouldBeAsync(HttpStatusCode.Conflict).ConfigureAwait(false);

        var conflictPayload = await replaceResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>()
            .ConfigureAwait(false);
        conflictPayload.Should().NotBeNull();
        conflictPayload!.Should().ContainKey("reason");
        conflictPayload["reason"].Should().Be("open_counts");

        using var mergeContent = CreateCsvContent(replaceLines);
        var mergeResponse = await PostImportAsync(adminClient, shopId, mergeContent, dryRun: false, mode: "merge")
            .ConfigureAwait(false);
        await mergeResponse.ShouldBeAsync(HttpStatusCode.OK).ConfigureAwait(false);

        await using var verifyConnection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        const string verifySql = "SELECT \"Sku\" FROM \"Product\" WHERE \"ShopId\" = @shopId ORDER BY \"Sku\";";
        await using var verifyCommand = new NpgsqlCommand(verifySql, verifyConnection)
        {
            Parameters = { new("shopId", shopId) }
        };

        var finalSkus = new List<string>();
        await using (var finalReader = await verifyCommand.ExecuteReaderAsync().ConfigureAwait(false))
        {
            while (await finalReader.ReadAsync().ConfigureAwait(false))
            {
                finalSkus.Add(finalReader.GetString(0));
            }
        }

        finalSkus.Should().BeEquivalentTo(new[] { "A-GOBBIO16", "A-KF63030", "A-NEW-REPLACE" });
    }

    private static StringContent CreateCsvContent(string[] lines)
    {
        var csv = string.Join('\n', lines);
        var content = new StringContent(csv, Encoding.Latin1, "text/csv");
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv")
        {
            CharSet = Encoding.Latin1.WebName
        };

        return content;
    }
}
