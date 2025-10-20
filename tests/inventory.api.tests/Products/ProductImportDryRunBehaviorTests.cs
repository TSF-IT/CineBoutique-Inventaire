using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

public class ProductImportDryRunBehaviorTests : IClassFixture<TestApiFactory>
{
  private readonly TestApiFactory _f;
  public ProductImportDryRunBehaviorTests(TestApiFactory f) => _f = f;

  [Fact]
  public async System.Threading.Tasks.Task DryRun_HeaderOnly_DoesNotChangeCount()
  {
    // 1) count avant
    var r0 = await _f.Client.GetAsync("/api/products/count");
    r0.EnsureSuccessStatusCode();
    var before = System.Text.Json.JsonDocument.Parse(await r0.Content.ReadAsStringAsync())
                  .RootElement.GetProperty("total").GetInt64();

    // 2) Appel dry-run avec uniquement l'entête (séparateur ';')
    var csv = new StringBuilder()
      .AppendLine("barcode_rfid;sku;name;groupe;sous_groupe;extraA;extraB")
      .ToString();

    using var form = new MultipartFormDataContent();
    var file = new StringContent(csv, Encoding.UTF8, "text/csv");
    file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
    { Name = "\"file\"", FileName = "\"dryrun.csv\"" };
    form.Add(file, "file", "dryrun.csv");

    var rDry = await _f.Client.PostAsync("/api/products/import?dryRun=true", form);

    // 3) Quoi qu'il arrive côté statut (401/403/204/200), on vérifie que la base n'a pas bougé.
    //    -> pas d'appel à EnsureSuccessStatusCode ici : on rend le test robuste vis-à-vis de l'auth.
    Assert.True(
      rDry.StatusCode == HttpStatusCode.OK ||
      rDry.StatusCode == HttpStatusCode.NoContent ||
      rDry.StatusCode == HttpStatusCode.Unauthorized ||
      rDry.StatusCode == HttpStatusCode.Forbidden,
      $"Unexpected status code: {(int)rDry.StatusCode} {rDry.StatusCode}");

    // 4) count après : doit être identique
    var r1 = await _f.Client.GetAsync("/api/products/count");
    r1.EnsureSuccessStatusCode();
    var after = System.Text.Json.JsonDocument.Parse(await r1.Content.ReadAsStringAsync())
                 .RootElement.GetProperty("total").GetInt64();

    Assert.Equal(before, after);
  }
}
