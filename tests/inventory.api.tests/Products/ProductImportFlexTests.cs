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

    using var dryRunScope = await _f.WithDbAsync(_ => Task.CompletedTask);

    var client = _f.Client;
    client.DefaultRequestHeaders.Remove("X-Admin");
    client.DefaultRequestHeaders.Add("X-Admin", "true");

    var csv = new StringBuilder()
      .AppendLine("barcode_rfid;sku;name;couleurSecondaire;packaging;libreX")
      .AppendLine("321000000002;CB-0002;Café Moulu;Vert;Sachet;foo")
      .ToString();

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
    using (var seedScope = await _f.WithDbAsync(async conn =>
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

    using var verifyScope = await _f.WithDbAsync(async conn =>
    {
      var shopId = await _f.GetOrCreateAnyShopIdAsync().ConfigureAwait(false);
      await _f.UpsertProductAsync(conn, shopId, "CB-0001", "Café Grains", "3210000000013", null).ConfigureAwait(false);
      const string seedAttributes = @"
UPDATE ""Product""
SET ""Attributes"" = CAST(@attrs AS jsonb)
WHERE ""Sku"" = @sku;";
      await conn.ExecuteAsync(seedAttributes, new { sku = "CB-0001", attrs = "{\"packaging\":\"Sachet\",\"origine\":\"Colombie\"}" }).ConfigureAwait(false);
    });

    var client = _f.Client;
    client.DefaultRequestHeaders.Remove("X-Admin");
    client.DefaultRequestHeaders.Add("X-Admin", "true");

    var csv1 = new StringBuilder()
      .AppendLine("barcode_rfid;sku;name;groupe;sous_groupe;couleurSecondaire;packaging")
      .AppendLine("321000000001;CB-0001;Café Grains 1kg;Cafe;Grains;Bleu;Sachet")
      .ToString();

    var csv2 = new StringBuilder()
      .AppendLine("barcode_rfid;sku;name;groupe;sous_groupe;origine")
      .AppendLine("321000000001;CB-0001;Café Grains 1kg;Cafe;Grains;Colombie")
      .ToString();

    using var content1 = new StringContent(csv1, Encoding.UTF8, "text/csv");
    var response1 = await client.PostAsync("/api/products/import", content1).ConfigureAwait(false);
    Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
    response1.Dispose();

    using var content2 = new StringContent(csv2, Encoding.UTF8, "text/csv");
    var response = await client.PostAsync("/api/products/import", content2).ConfigureAwait(false);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var payload = await response.Content.ReadFromJsonAsync<ProductImportResponse>().ConfigureAwait(false);
    Assert.NotNull(payload);
    Assert.False(payload!.DryRun);

    // 3) Vérifie en base : Attributes contient TOUT (merge non destructif) — sans reset DB
    await _f.WithDbNoResetAsync(async conn =>
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
