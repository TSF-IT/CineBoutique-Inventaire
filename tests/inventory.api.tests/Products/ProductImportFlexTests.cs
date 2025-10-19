using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using Dapper;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("db")]
public sealed class ProductImportFlexTests : IClassFixture<TestApiFactory>
{
  private readonly TestApiFactory _f;

  public ProductImportFlexTests(TestApiFactory fixture) => _f = fixture;

  [SkippableFact]
  public async Task Import_Flex_DryRun_ReportsUnknownColumns()
  {
    Skip.If(!_f.IsAvailable, _f.SkipReason ?? "Backend d'intégration indisponible.");

    using var scope = await _f.WithDbAsync(_ => Task.CompletedTask);

    var client = _f.Client;
    client.DefaultRequestHeaders.Remove("X-Admin");
    client.DefaultRequestHeaders.Add("X-Admin", "true");

    var csv = "\"barcode_rfid\";\"item\";\"descr\";\"sous_groupe\";\"couleurSecondaire\"\n" +
              "\"3210000000013\";\"CB-0001\";\"Café Grains\";\"Cafés grains\";\"Bleu\"";

    using var content = new StringContent(csv, Encoding.UTF8, "text/csv");
    var response = await client.PostAsync("/api/products/import?dryRun=true", content).ConfigureAwait(false);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
    Assert.NotNull(payload);
    Assert.True(payload!.DryRun);
    Assert.Contains("couleurSecondaire", payload.UnknownColumns, StringComparer.OrdinalIgnoreCase);
  }

  [SkippableFact]
  public async Task Import_Flex_MergeAttributes_IsNonDestructive()
  {
    Skip.If(!_f.IsAvailable, _f.SkipReason ?? "Backend d'intégration indisponible.");

    // --- Arrange taxonomie : on crée/maj le groupe "Cafe" et le sous-groupe "Grains"
    using (var scope = await _f.WithDbAsync(async conn =>
    {
        // parent: "Cafe"
        var parentId = await conn.ExecuteScalarAsync<long>(@"
      WITH upsert AS (
        UPDATE ""ProductGroup"" SET ""Label"" = 'Cafe'
        WHERE ""Code"" = 'cafe'
        RETURNING ""Id""
      )
      INSERT INTO ""ProductGroup"" (""Code"",""Label"")
      SELECT 'cafe','Cafe'
      WHERE NOT EXISTS (SELECT 1 FROM upsert)
      RETURNING ""Id"";");

        // enfant: "Grains" rattaché à "Cafe"
        await conn.ExecuteAsync(@"
      WITH upsert AS (
        UPDATE ""ProductGroup""
        SET ""Label"" = 'Grains', ""ParentId"" = @pid
        WHERE ""Code"" = 'grains'
        RETURNING ""Id""
      )
      INSERT INTO ""ProductGroup"" (""Code"",""Label"",""ParentId"")
      SELECT 'grains','Grains',@pid
      WHERE NOT EXISTS (SELECT 1 FROM upsert);", new { pid = parentId });
    }))
    { /* scope dispose */ }

    using var scope = await _f.WithDbAsync(async conn =>
    {
      await _f.UpsertProductAsync(conn, "CB-0001", "Café Grains", "3210000000013", null).ConfigureAwait(false);
      const string seedAttributes = @"
UPDATE ""Product""
SET ""Attributes"" = CAST(@attrs AS jsonb)
WHERE ""Sku"" = @sku;";
      await conn.ExecuteAsync(seedAttributes, new { sku = "CB-0001", attrs = "{\"packaging\":\"Sachet\",\"origine\":\"Colombie\"}" }).ConfigureAwait(false);
    });

    var client = _f.Client;
    client.DefaultRequestHeaders.Remove("X-Admin");
    client.DefaultRequestHeaders.Add("X-Admin", "true");

    var csv = "\"barcode_rfid\";\"item\";\"descr\";\"sous_groupe\";\"couleurSecondaire\"\n" +
              "\"3210000000013\";\"CB-0001\";\"Café Grains\";\"Cafés grains\";\"Bleu\"";

    using var content = new StringContent(csv, Encoding.UTF8, "text/csv");
    var response = await client.PostAsync("/api/products/import", content).ConfigureAwait(false);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
    Assert.NotNull(payload);
    Assert.False(payload!.DryRun);

    // 3) Vérifie en base : Attributes contient TOUT (merge non destructif)
    using var _ = await _f.WithDbAsync(async conn =>
    {
      var js = await conn.ExecuteScalarAsync<string>(@"
    SELECT COALESCE(CAST(""Attributes"" AS text), '{}')
    FROM ""Product""
    WHERE ""Sku"" = 'CB-0001';");

      Assert.False(string.IsNullOrWhiteSpace(js), "Attributes should not be empty");
      Assert.Contains("\"couleurSecondaire\"", js, System.StringComparison.OrdinalIgnoreCase);
      Assert.Contains("Bleu", js, System.StringComparison.OrdinalIgnoreCase);
      Assert.Contains("\"packaging\"", js, System.StringComparison.OrdinalIgnoreCase);
      Assert.Contains("Sachet", js, System.StringComparison.OrdinalIgnoreCase);
      Assert.Contains("\"origine\"", js, System.StringComparison.OrdinalIgnoreCase);
      Assert.Contains("Colombie", js, System.StringComparison.OrdinalIgnoreCase);
    });
  }
}
