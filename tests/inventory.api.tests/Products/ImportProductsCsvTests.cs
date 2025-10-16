using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
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
        payload!.Inserted.Should().Be(5);
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
        reimportPayload!.Inserted.Should().Be(2);
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
        payload!.Inserted.Should().Be(1);
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
