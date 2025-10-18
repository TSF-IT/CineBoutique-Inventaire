using System.Net;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using Dapper;
using Xunit;

[Collection("db")]
public class ProductSuggestTests : IClassFixture<TestApiFactory>
{
  private readonly TestApiFactory _f;
  public ProductSuggestTests(TestApiFactory f) => _f = f;

  [SkippableFact]
  public async Task Suggest_BySkuPrefix_And_ByNameFragment_Works()
  {
    Skip.If(!_f.IsAvailable, _f.SkipReason ?? "Backend d'intégration indisponible.");

    using var scope = await _f.WithDbAsync(async conn =>
    {
      // Arrange: insère 2 produits et un sousGroupe
      var gid = await conn.ExecuteScalarAsync<long?>(@"
WITH upsert AS (
  UPDATE ""ProductGroup""
  SET ""Label"" = @label
  WHERE ""Code"" = @code
  RETURNING ""Id""
)
INSERT INTO ""ProductGroup"" (""Code"",""Label"")
SELECT @code, @label
WHERE NOT EXISTS (SELECT 1 FROM upsert)
RETURNING ""Id"";", new { code = "cafe", label = "Café" });
      await conn.ExecuteAsync(@"
        INSERT INTO ""Product"" (""Sku"",""Name"",""Ean"",""GroupId"") VALUES
        ('CB-0001','Café Grains 1kg','321000000001',@gid),
        ('CB-0002','Café Moulu','321000000002',@gid)
      ON CONFLICT (""Sku"") DO NOTHING;", new { gid });
    });

    // Act
    var r1 = await _f.Client.GetAsync("/api/products/suggest?q=CB-000");
    var r2 = await _f.Client.GetAsync("/api/products/suggest?q=cafe");

    // Assert
    Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
    Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    var a = await r1.Content.ReadAsStringAsync();
    var b = await r2.Content.ReadAsStringAsync();
    Assert.Contains("CB-0001", a);
    Assert.Contains("Café", b);
  }
}
