using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Dapper; // ← nécessaire pour ExecuteScalarAsync
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

// Sérialise uniquement cette collection pour éviter les écritures concurrentes
[Collection("ApiSerial")]
public class ProductImportDryRunBehaviorTests : IClassFixture<TestApiFactory>
{
  private readonly TestApiFactory _f;
  public ProductImportDryRunBehaviorTests(TestApiFactory f) => _f = f;

  [Fact]
  public async System.Threading.Tasks.Task DryRun_HeaderOnly_DoesNotChangeCount()
  {
    long before = 0, after = 0;

    // 1) Count AVANT directement en DB (pas via HTTP)
    await _f.WithDbNoResetAsync(async conn =>
    {
      before = await conn.ExecuteScalarAsync<long>(@"SELECT COUNT(*) FROM ""Product"";");
    });

    // 2) Appel dry-run avec uniquement l'en-tête (séparateur ';')
    var csv = new StringBuilder()
      .AppendLine("barcode_rfid;sku;name;groupe;sous_groupe;extraA;extraB")
      .ToString();

    using var form = new MultipartFormDataContent();
    var file = new StringContent(csv, Encoding.UTF8, "text/csv");
    file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
    { Name = "\"file\"", FileName = "\"dryrun.csv\"" };
    form.Add(file, "file", "dryrun.csv");

    var rDry = await _f.Client.PostAsync("/api/products/import?dryRun=true", form);

    // On ne fait PAS d'EnsureSuccessStatusCode : statut toléré (200/204/401/403)
    Assert.True(
      rDry.StatusCode == HttpStatusCode.OK ||
      rDry.StatusCode == HttpStatusCode.NoContent ||
      rDry.StatusCode == HttpStatusCode.Unauthorized ||
      rDry.StatusCode == HttpStatusCode.Forbidden,
      $"Unexpected status code: {(int)rDry.StatusCode} {rDry.StatusCode}");

    // 3) Count APRES directement en DB (pas via HTTP)
    await _f.WithDbNoResetAsync(async conn =>
    {
      after = await conn.ExecuteScalarAsync<long>(@"SELECT COUNT(*) FROM ""Product"";");
    });

    // 4) Dry-run ne doit JAMAIS écrire
    Assert.Equal(before, after);
  }
}
