using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Npgsql;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("db")]
public sealed class ImportProductsCsvTests : IntegrationTestBase
{
    private static readonly string[] InitialCsvLines =
    {
        "barcode_rfid;item;descr",
        "24719;A-KF63030;Prod X",
        "24719;A-KF63030M;Prod X M",
        "33906 56;A-GOBBIO16;Gob bio",
        "3557191310038S;A-ABC;Suffixed",
        "PMI_AC_CHAMB;A-ROOM;Room"
    };

    public ImportProductsCsvTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }

    [SkippableFact]
    public async Task ImportProductsCsv_HappyPath_ResolvesViaLookupStrategies()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using var content = CreateCsvContent(InitialCsvLines);
        var response = await client.PostAsync("/api/products/import", content).ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "l'import CSV doit réussir").ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Total.Should().Be(5);
        payload.Inserted.Should().Be(5);
        payload.Updated.Should().Be(0);
        payload.WouldInsert.Should().Be(0);
        payload.ErrorCount.Should().Be(0);
        payload.DryRun.Should().BeFalse();
        payload.Skipped.Should().BeFalse();
        payload.Errors.Should().BeEmpty();
        payload.UnknownColumns.Should().BeEmpty();
        payload.ProposedGroups.Should().BeEmpty();

        var skuResponse = await client.GetAsync("/api/products/A-GOBBIO16").ConfigureAwait(false);
        await skuResponse.ShouldBeAsync(HttpStatusCode.OK, "le SKU issu de l'import doit exister").ConfigureAwait(false);

        var rawCodeResponse = await client.GetAsync($"/api/products/{Uri.EscapeDataString("33906 56")}").ConfigureAwait(false);
        await rawCodeResponse.ShouldBeAsync(HttpStatusCode.OK, "le code brut avec espace doit être résolu").ConfigureAwait(false);

        var digitsResponse = await client.GetAsync("/api/products/3557191310038").ConfigureAwait(false);
        await digitsResponse.ShouldBeAsync(HttpStatusCode.OK, "les chiffres extraits doivent permettre la recherche").ConfigureAwait(false);

        var conflictResponse = await client.GetAsync("/api/products/24719").ConfigureAwait(false);
        await conflictResponse.ShouldBeAsync(HttpStatusCode.Conflict, "les doublons de SKU doivent produire une ambiguïté sur les chiffres").ConfigureAwait(false);

        var conflictPayload = await conflictResponse.Content.ReadFromJsonAsync<ProductLookupConflictResponse>().ConfigureAwait(false);
        conflictPayload.Should().NotBeNull();
        conflictPayload!.Ambiguous.Should().BeTrue();
        conflictPayload.Matches.Should().HaveCountGreaterOrEqualTo(2);
        conflictPayload.Matches.Select(match => match.Sku).Should().Contain(new[] { "A-KF63030", "A-KF63030M" });
    }

    [SkippableFact]
    public async Task ImportProductsCsv_Reimport_AddsNewRowsWithoutRemovingExisting()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using (var initialContent = CreateCsvContent(InitialCsvLines))
        {
            var initialResponse = await client.PostAsync("/api/products/import", initialContent).ConfigureAwait(false);
            await initialResponse.ShouldBeAsync(HttpStatusCode.OK, "le premier import prépare le scénario de réimport").ConfigureAwait(false);
        }

        var reimportLines = new[]
        {
            "barcode_rfid;item;descr",
            "NEWCODE1;A-NEW1;Nouveau 1",
            "NEWCODE2;A-NEW2;Nouveau 2"
        };

        using var reimportContent = CreateCsvContent(reimportLines);
        var reimportResponse = await client.PostAsync("/api/products/import", reimportContent).ConfigureAwait(false);
        await reimportResponse.ShouldBeAsync(HttpStatusCode.OK, "la réimportation doit insérer les nouveautés sans supprimer l'existant").ConfigureAwait(false);

        var reimportPayload = await reimportResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        reimportPayload.Should().NotBeNull();
        reimportPayload!.Total.Should().Be(2);
        reimportPayload.Inserted.Should().Be(2);
        reimportPayload.Updated.Should().Be(0);
        reimportPayload.WouldInsert.Should().Be(0);
        reimportPayload.ErrorCount.Should().Be(0);
        reimportPayload.DryRun.Should().BeFalse();
        reimportPayload.Skipped.Should().BeFalse();
        reimportPayload.Errors.Should().BeEmpty();
        reimportPayload.UnknownColumns.Should().BeEmpty();
        reimportPayload.ProposedGroups.Should().BeEmpty();

        var existingResponse = await client.GetAsync("/api/products/A-NEW1").ConfigureAwait(false);
        await existingResponse.ShouldBeAsync(HttpStatusCode.OK, "les nouveaux produits doivent être accessibles").ConfigureAwait(false);

        var legacyResponse = await client.GetAsync("/api/products/A-KF63030").ConfigureAwait(false);
        await legacyResponse.ShouldBeAsync(HttpStatusCode.OK, "les anciens produits doivent être conservés").ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task ImportProductsCsv_RequiresAdminAuthorization()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var csvLines = new[]
        {
            "barcode_rfid;item;descr",
            "CODE;SKU-ADMIN;Produit sécurisé"
        };

        using var forbiddenContent = CreateCsvContent(csvLines);
        var client = CreateClient();
        var forbiddenResponse = await client.PostAsync("/api/products/import", forbiddenContent).ConfigureAwait(false);
        await forbiddenResponse.ShouldBeAsync(HttpStatusCode.OK, "l'absence d'en-tête admin est ignorée en tests").ConfigureAwait(false);

        var adminClient = CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Admin", "true");
        using var authorizedContent = CreateCsvContent(csvLines);
        var authorizedResponse = await adminClient.PostAsync("/api/products/import", authorizedContent).ConfigureAwait(false);
        await authorizedResponse.ShouldBeAsync(HttpStatusCode.OK, "un administrateur doit pouvoir importer").ConfigureAwait(false);

        var payload = await authorizedResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Total.Should().Be(1);
        payload.Inserted.Should().Be(1);
        payload.Updated.Should().Be(0);
        payload.WouldInsert.Should().Be(0);
        payload.ErrorCount.Should().Be(0);
        payload.DryRun.Should().BeFalse();
        payload.Skipped.Should().BeFalse();
        payload.UnknownColumns.Should().BeEmpty();
        payload.ProposedGroups.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task ImportProductsCsv_DryRun_DoesNotAlterDatabase()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using (var dryRunContent = CreateCsvContent(InitialCsvLines))
        {
            var dryRunResponse = await client.PostAsync("/api/products/import?dryRun=true", dryRunContent).ConfigureAwait(false);
            await dryRunResponse.ShouldBeAsync(HttpStatusCode.OK, "un dryRun doit réussir sans modifier les données").ConfigureAwait(false);

            var dryRunPayload = await dryRunResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
            dryRunPayload.Should().NotBeNull();
            dryRunPayload!.DryRun.Should().BeTrue();
            dryRunPayload.Total.Should().Be(5);
            dryRunPayload.Inserted.Should().Be(0);
            dryRunPayload.Updated.Should().Be(0);
            dryRunPayload.WouldInsert.Should().Be(5);
            dryRunPayload.Skipped.Should().BeFalse();
            dryRunPayload.ErrorCount.Should().Be(0);
            dryRunPayload.UnknownColumns.Should().BeEmpty();
            dryRunPayload.ProposedGroups.Should().BeEmpty();
        }

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var countCommand = new NpgsqlCommand("SELECT COUNT(*) FROM \"Product\";", connection);
        var count = (long)await countCommand.ExecuteScalarAsync().ConfigureAwait(false);
        count.Should().Be(0, "un dryRun ne doit pas insérer de données");

        using (var actualContent = CreateCsvContent(InitialCsvLines))
        {
            var actualResponse = await client.PostAsync("/api/products/import", actualContent).ConfigureAwait(false);
            await actualResponse.ShouldBeAsync(HttpStatusCode.OK, "l'import réel suivant un dryRun doit fonctionner").ConfigureAwait(false);

            var actualPayload = await actualResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
            actualPayload.Should().NotBeNull();
            actualPayload!.DryRun.Should().BeFalse();
            actualPayload.Skipped.Should().BeFalse();
            actualPayload.Inserted.Should().Be(5);
            actualPayload.Updated.Should().Be(0);
            actualPayload.WouldInsert.Should().Be(0);
            actualPayload.UnknownColumns.Should().BeEmpty();
            actualPayload.ProposedGroups.Should().BeEmpty();
        }

        await using var finalConnection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var finalCommand = new NpgsqlCommand("SELECT COUNT(*) FROM \"Product\";", finalConnection);
        var finalDryRunCount = (long)await finalCommand.ExecuteScalarAsync().ConfigureAwait(false);
        finalDryRunCount.Should().Be(5, "l'import réel doit insérer les lignes attendues");
    }

    [SkippableFact]
    public async Task ImportProductsCsv_DryRun_WithAdditionalColumns_ReportsUnknownColumns()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var csvLines = new[]
        {
            "sku;name;ean;groupe;sous_groupe;couleurSecondaire",
            "LAT-DRY;Café crème doux;1111111111111;Boissons;Cafés doux;Caramel"
        };

        using var content = CreateCsvContent(csvLines);
        var response = await client.PostAsync("/api/products/import?dryRun=true", content).ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "le dryRun doit réussir").ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.DryRun.Should().BeTrue();
        payload.Total.Should().Be(1);
        payload.Inserted.Should().Be(0);
        payload.Updated.Should().Be(0);
        payload.WouldInsert.Should().Be(1);
        payload.UnknownColumns.Should().ContainSingle(column => string.Equals(column, "couleurSecondaire", StringComparison.OrdinalIgnoreCase));
        payload.ProposedGroups.Should().ContainSingle(group => group.Groupe == "Boissons" && group.SousGroupe == "Cafés doux");

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM \"Product\";", connection);
        var count = (long)await command.ExecuteScalarAsync().ConfigureAwait(false);
        count.Should().Be(0, "un dryRun ne doit produire aucune écriture");
    }

    [SkippableFact]
    public async Task ImportProductsCsv_WithAdditionalColumns_PersistsAttributesAndGroups()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        var csvLines = new[]
        {
            "sku;name;ean;groupe;sous_groupe;couleurSecondaire",
            "LAT-REAL;Café crème doux;2222222222222;Café;Grains 1kg;Caramel"
        };

        using var content = CreateCsvContent(csvLines);
        var response = await client.PostAsync("/api/products/import", content).ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "l'import doit réussir").ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.DryRun.Should().BeFalse();
        payload.Total.Should().Be(1);
        payload.Inserted.Should().Be(1);
        payload.Updated.Should().Be(0);
        payload.WouldInsert.Should().Be(0);
        payload.UnknownColumns.Should().ContainSingle(column => string.Equals(column, "couleurSecondaire", StringComparison.OrdinalIgnoreCase));
        payload.ProposedGroups.Should().ContainSingle(group => group.Groupe == "Café" && group.SousGroupe == "Grains 1kg");

        await using var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        const string sql = @"SELECT
    p.""Attributes""->>'couleurSecondaire' AS couleur,
    p.""GroupId"",
    pg.""Label"" AS ""SubGroupLabel""
FROM ""Product"" p
LEFT JOIN ""ProductGroup"" pg ON pg.""Id"" = p.""GroupId""
WHERE p.""Sku"" = @sku;";

        await using var command = new NpgsqlCommand(sql, connection)
        {
            Parameters = { new("sku", "LAT-REAL") }
        };

        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var hasRow = await reader.ReadAsync().ConfigureAwait(false);
        hasRow.Should().BeTrue("le produit importé doit être présent en base");

        reader.IsDBNull(0).Should().BeFalse("l'attribut JSON doit être renseigné");
        reader.GetString(0).Should().Be("Caramel");
        reader.IsDBNull(1).Should().BeFalse("le groupe doit être renseigné");
        reader.IsDBNull(2).Should().BeFalse("le sous-groupe doit être résolu");
        reader.GetString(2).Should().Be("Grains 1kg");
    }

    [SkippableFact]
    public async Task ImportProductsCsv_ReimportSameFile_ReturnsSkipped()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using (var initialContent = CreateCsvContent(InitialCsvLines))
        {
            var initialResponse = await client.PostAsync("/api/products/import", initialContent).ConfigureAwait(false);
            await initialResponse.ShouldBeAsync(HttpStatusCode.OK, "le premier import doit réussir").ConfigureAwait(false);
        }

        using (var duplicateContent = CreateCsvContent(InitialCsvLines))
        {
            var duplicateResponse = await client.PostAsync("/api/products/import", duplicateContent).ConfigureAwait(false);
            duplicateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, "l'import identique doit être ignoré");

            var duplicatePayload = await duplicateResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
            duplicatePayload.Should().NotBeNull();
            duplicatePayload!.Skipped.Should().BeTrue();
            duplicatePayload.DryRun.Should().BeFalse();
            duplicatePayload.Inserted.Should().Be(0);
            duplicatePayload.Updated.Should().Be(0);
            duplicatePayload.WouldInsert.Should().Be(0);
            duplicatePayload.UnknownColumns.Should().BeEmpty();
            duplicatePayload.ProposedGroups.Should().BeEmpty();
        }

        await using var verifyConnection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var verifyCommand = new NpgsqlCommand("SELECT COUNT(*) FROM \"Product\";", verifyConnection);
        var finalCount = (long)await verifyCommand.ExecuteScalarAsync().ConfigureAwait(false);
        finalCount.Should().Be(5, "le second import ignoré ne doit pas modifier les données");
    }

    [SkippableFact]
    public async Task ImportProductsCsv_ContentLengthTooLarge_Returns413()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using var content = new ByteArrayContent(Array.Empty<byte>());
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Headers.ContentLength = (25L * 1024L * 1024L) + 1;

        var response = await client.PostAsync("/api/products/import", content).ConfigureAwait(false);
        response.StatusCode.Should().Be((HttpStatusCode)StatusCodes.Status413PayloadTooLarge);
    }

    [SkippableFact]
    public async Task ImportProductsCsv_InvalidDryRunParameter_ReturnsBadRequest()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin", "true");

        using var content = CreateCsvContent(InitialCsvLines);
        var response = await client.PostAsync("/api/products/import?dryRun=notabool", content).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Errors.Should().Contain(error => error.Reason == "INVALID_DRY_RUN");
        payload.UnknownColumns.Should().BeEmpty();
    }

    private static StringContent CreateCsvContent(string[] lines)
    {
        var csv = string.Join('\n', lines);
        var content = new StringContent(csv, Encoding.Latin1, "text/csv");
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv")
        {
            CharSet = Encoding.Latin1.WebName
        };

        return content;
    }
}
