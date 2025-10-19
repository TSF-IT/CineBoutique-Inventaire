using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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

    // import minimal (sans groupe) => doit créer un produit (GroupId null autorisé)
    var csv = new StringBuilder()
      .AppendLine("barcode_rfid;sku;name")
      .AppendLine("321000000999;ZZ-TEST-COUNT;Produit Count Test")
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
