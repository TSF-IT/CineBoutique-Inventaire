using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Dapper;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

public class ProductAdminEndpointsTests : IClassFixture<TestApiFactory>
{
  private readonly TestApiFactory _f;
  public ProductAdminEndpointsTests(TestApiFactory f) => _f = f;

  [Fact]
  public async System.Threading.Tasks.Task Count_Reflects_Import_Increment()
  {
    // count avant
    var r0 = await _f.Client.GetAsync("/api/products/count");
    r0.EnsureSuccessStatusCode();
    var before = System.Text.Json.JsonDocument.Parse(await r0.Content.ReadAsStringAsync())
                  .RootElement.GetProperty("total").GetInt64();

    // --- Arrange taxonomie : groupe "Cafe" + sous-groupe "Grains" (SANS reset DB)
    await _f.WithDbNoResetAsync(async conn =>
    {
      var parentId = await conn.ExecuteScalarAsync<long>(@"
    WITH upsert AS (
      UPDATE ""ProductGroup"" SET ""Label""='Cafe'
      WHERE ""Code""='cafe'
      RETURNING ""Id""
    )
    INSERT INTO ""ProductGroup"" (""Code"",""Label"")
    SELECT 'cafe','Cafe'
    WHERE NOT EXISTS (SELECT 1 FROM upsert)
    RETURNING ""Id"";");

      await conn.ExecuteAsync(@"
    WITH upsert AS (
      UPDATE ""ProductGroup""
      SET ""Label""='Grains', ""ParentId""=@pid
      WHERE ""Code""='grains'
      RETURNING ""Id""
    )
    INSERT INTO ""ProductGroup"" (""Code"",""Label"",""ParentId"")
    SELECT 'grains','Grains',@pid
    WHERE NOT EXISTS (SELECT 1 FROM upsert);", new { pid = parentId });
    });

    // --- Import CSV (séparateur ';') avec groupe/sous_groupe pour garantir l'insert
    var csv = new StringBuilder()
      .AppendLine("barcode_rfid;sku;name;groupe;sous_groupe")
      .AppendLine("321000000999;ZZ-TEST-COUNT;Produit Count Test;Cafe;Grains")
      .ToString();

    using var form = new MultipartFormDataContent();
    var file = new StringContent(csv, Encoding.UTF8, "text/csv");
    file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
    { Name = "\"file\"", FileName = "\"import-count.csv\"" };
    form.Add(file, "file", "import-count.csv");

    var rImport = await _f.Client.PostAsync("/api/products/import?dryRun=false", form);
    rImport.EnsureSuccessStatusCode();

    // count après
    var r1 = await _f.Client.GetAsync("/api/products/count");
    r1.EnsureSuccessStatusCode();
    var after = System.Text.Json.JsonDocument.Parse(await r1.Content.ReadAsStringAsync())
                 .RootElement.GetProperty("total").GetInt64();

    Assert.Equal(before + 1, after);
  }
}
