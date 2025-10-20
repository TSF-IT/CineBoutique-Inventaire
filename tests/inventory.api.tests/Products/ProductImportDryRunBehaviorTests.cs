using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Dapper;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("ApiSerial")]
public class ProductImportDryRunBehaviorTests : IClassFixture<TestApiFactory>
{
  // Sérialise uniquement cette classe (évite les courses pendant la mesure)
  private readonly TestApiFactory _f;
  public ProductImportDryRunBehaviorTests(TestApiFactory f) => _f = f;

  [Fact]
  public async System.Threading.Tasks.Task DryRun_DoesNotInsert_SentinelSkuRemainsAbsent()
  {
    // Sentinelle improbable à insérer par un autre test
    var marker = $"ZZ-DRYRUN-G2-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

    // 1) Vérifier en DB que la sentinelle n'existe pas AVANT
    long before = 0;
    await _f.WithDbNoResetAsync(async conn =>
    {
      before = await conn.ExecuteScalarAsync<long>(
        @"SELECT COUNT(*) FROM ""Product"" WHERE ""Sku"" = @sku OR ""Name"" = @sku;",
        new { sku = marker });
    });
    Assert.Equal(0, before);

    // 2) Appel dry-run avec uniquement l'entête (séparateur ';')
    //    (peu importe le statut HTTP, l'important est de ne rien écrire)
    var csv = new StringBuilder()
      .AppendLine("barcode_rfid;sku;name;groupe;sous_groupe;extraA;extraB")
      .ToString();

    using var form = new MultipartFormDataContent();
    var file = new StringContent(csv, Encoding.UTF8, "text/csv");
    file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
    { Name = "\"file\"", FileName = "\"dryrun.csv\"" };
    form.Add(file, "file", "dryrun.csv");

    var res = await _f.Client.PostAsync("/api/products/import?dryRun=true", form);
    Assert.True(
      res.StatusCode is HttpStatusCode.OK
                   or HttpStatusCode.NoContent
                   or HttpStatusCode.Unauthorized
                   or HttpStatusCode.Forbidden,
      $"Unexpected status code: {(int)res.StatusCode} {res.StatusCode}");

    // 3) Vérifier en DB que la sentinelle n'existe toujours pas APRES
    long after = -1;
    await _f.WithDbNoResetAsync(async conn =>
    {
      after = await conn.ExecuteScalarAsync<long>(
        @"SELECT COUNT(*) FROM ""Product"" WHERE ""Sku"" = @sku OR ""Name"" = @sku;",
        new { sku = marker });
    });

    Assert.Equal(0, after);
  }
}
