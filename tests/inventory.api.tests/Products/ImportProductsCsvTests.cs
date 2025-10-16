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
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("api-tests")]
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
        payload.WouldInsert.Should().Be(5);
        payload.ErrorCount.Should().Be(0);
        payload.DryRun.Should().BeFalse();
        payload.Skipped.Should().BeFalse();
        payload.Errors.Should().BeEmpty();

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
    public async Task ImportProductsCsv_Reimport_TruncatesPreviousRows()
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
        await reimportResponse.ShouldBeAsync(HttpStatusCode.OK, "la réimportation doit remplacer les produits").ConfigureAwait(false);

        var reimportPayload = await reimportResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        reimportPayload.Should().NotBeNull();
        reimportPayload!.Total.Should().Be(2);
        reimportPayload.Inserted.Should().Be(2);
        reimportPayload.WouldInsert.Should().Be(2);
        reimportPayload.ErrorCount.Should().Be(0);
        reimportPayload.DryRun.Should().BeFalse();
        reimportPayload.Skipped.Should().BeFalse();
        reimportPayload.Errors.Should().BeEmpty();

        var missingResponse = await client.GetAsync("/api/products/A-KF63030").ConfigureAwait(false);
        await missingResponse.ShouldBeAsync(HttpStatusCode.NotFound, "la table doit avoir été tronquée").ConfigureAwait(false);

        var existingResponse = await client.GetAsync("/api/products/A-NEW1").ConfigureAwait(false);
        await existingResponse.ShouldBeAsync(HttpStatusCode.OK, "les nouveaux produits doivent être accessibles").ConfigureAwait(false);
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
        await forbiddenResponse.ShouldBeAsync(HttpStatusCode.Forbidden, "l'absence d'en-tête admin doit bloquer l'import").ConfigureAwait(false);

        var adminClient = CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Admin", "true");
        using var authorizedContent = CreateCsvContent(csvLines);
        var authorizedResponse = await adminClient.PostAsync("/api/products/import", authorizedContent).ConfigureAwait(false);
        await authorizedResponse.ShouldBeAsync(HttpStatusCode.OK, "un administrateur doit pouvoir importer").ConfigureAwait(false);

        var payload = await authorizedResponse.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
        payload.Should().NotBeNull();
        payload!.Total.Should().Be(1);
        payload.Inserted.Should().Be(1);
        payload.WouldInsert.Should().Be(1);
        payload.ErrorCount.Should().Be(0);
        payload.DryRun.Should().BeFalse();
        payload.Skipped.Should().BeFalse();
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
            dryRunPayload.WouldInsert.Should().Be(5);
            dryRunPayload.Skipped.Should().BeFalse();
            dryRunPayload.ErrorCount.Should().Be(0);
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
        }

        await using var finalConnection = await Fixture.OpenConnectionAsync().ConfigureAwait(false);
        await using var finalCommand = new NpgsqlCommand("SELECT COUNT(*) FROM \"Product\";", finalConnection);
        var finalDryRunCount = (long)await finalCommand.ExecuteScalarAsync().ConfigureAwait(false);
        finalDryRunCount.Should().Be(5, "l'import réel doit insérer les lignes attendues");
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
