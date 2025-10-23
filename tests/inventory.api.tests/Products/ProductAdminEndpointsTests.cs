using System;
using System.Collections.Generic;
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

    // --- Arrange : upsert taxonomie et insertion DB d'UN produit (aucun appel HTTP ici) ---
    var sku = $"ZZ-COUNT-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    await _f.WithDbNoResetAsync(async conn =>
    {
      var shopId = await conn.ExecuteScalarAsync<Guid>(@"SELECT ""Id"" FROM ""Shop"" ORDER BY LOWER(""Name""), ""Id"" LIMIT 1;");
      // Upsert groupe parent "Cafe"
      var parentId = await conn.ExecuteScalarAsync<long>(@"
    WITH upsert AS (
      UPDATE ""ProductGroup""
      SET ""Label""='Cafe'
      WHERE ""Code""='cafe'
      RETURNING ""Id""
    )
    INSERT INTO ""ProductGroup"" (""Code"",""Label"")
    SELECT 'cafe','Cafe'
    WHERE NOT EXISTS (SELECT 1 FROM upsert)
    RETURNING ""Id"";");

      // Upsert sous-groupe "Grains" rattaché à "Cafe"
      var gid = await conn.ExecuteScalarAsync<long>(@"
    WITH upsert AS (
      UPDATE ""ProductGroup""
      SET ""Label""='Grains', ""ParentId""=@pid
      WHERE ""Code""='grains'
      RETURNING ""Id""
    )
    INSERT INTO ""ProductGroup"" (""Code"",""Label"",""ParentId"")
    SELECT 'grains','Grains',@pid
    WHERE NOT EXISTS (SELECT 1 FROM upsert)
    RETURNING ""Id"";", new { pid = parentId });

      // Détection runtime des colonnes de timestamp
      var hasCreated = await conn.ExecuteScalarAsync<object>(@"
    select 1 from information_schema.columns
    where table_schema='public' and table_name='Product' and column_name='CreatedAtUtc'
    limit 1;") is not null;

      var hasUpdated = await conn.ExecuteScalarAsync<object>(@"
    select 1 from information_schema.columns
    where table_schema='public' and table_name='Product' and column_name='UpdatedAtUtc'
    limit 1;") is not null;

      // Construit l'INSERT selon les colonnes réellement présentes
      var cols = new List<string> { "\"ShopId\"", "\"Sku\"", "\"Name\"", "\"Ean\"", "\"GroupId\"", "\"Attributes\"" };
      var vals = new List<string> { "@ShopId", "@Sku", "@Name", "@Ean", "@Gid", "'{}'::jsonb" };

      if (hasCreated) { cols.Add("\"CreatedAtUtc\""); vals.Add("NOW() AT TIME ZONE 'UTC'"); }
      if (hasUpdated) { cols.Add("\"UpdatedAtUtc\""); vals.Add("NOW() AT TIME ZONE 'UTC'"); }

      var insertSql = $@"
    INSERT INTO ""Product"" ({string.Join(", ", cols)})
    VALUES ({string.Join(", ", vals)});";

      await conn.ExecuteAsync(insertSql, new {
        ShopId = shopId,
        Sku = sku,
        Name = "Produit Count Test",
        Ean = "3210000999999",
        Gid = gid
      });
    });

    // count après
    var r1 = await _f.Client.GetAsync("/api/products/count");
    r1.EnsureSuccessStatusCode();
    var after = System.Text.Json.JsonDocument.Parse(await r1.Content.ReadAsStringAsync())
                 .RootElement.GetProperty("total").GetInt64();

    Assert.Equal(before + 1, after);
  }
}
