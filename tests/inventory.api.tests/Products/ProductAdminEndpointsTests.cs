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

  [Fact]
  public async System.Threading.Tasks.Task Shop_Product_Count_Includes_Counted_References()
  {
    Skip.If(!_f.IsAvailable, _f.SkipReason ?? "Backend d'intégration indisponible.");

    var shopId = Guid.NewGuid();
    var locationId = Guid.NewGuid();
    var sku = $"Z-SKU-{Guid.NewGuid():N}".Substring(0, 12).ToUpperInvariant();

    using var scope = await _f.WithDbAsync(async conn =>
    {
      await conn.ExecuteAsync(
        @"INSERT INTO ""Shop"" (""Id"", ""Name"", ""Kind"") VALUES (@Id, @Name, 'boutique');",
        new { Id = shopId, Name = $"Boutique Comptage {Guid.NewGuid():N}" }).ConfigureAwait(false);

      await conn.ExecuteAsync(
        @"INSERT INTO ""Location"" (""Id"", ""ShopId"", ""Code"", ""Label"", ""Disabled"")
          VALUES (@Id, @ShopId, @Code, @Label, FALSE);",
        new
        {
          Id = locationId,
          ShopId = shopId,
          Code = $"ZCNT-{Guid.NewGuid():N}".Substring(0, 8).ToUpperInvariant(),
          Label = "Zone Comptage"
        }).ConfigureAwait(false);

      await _f.UpsertProductAsync(conn, shopId, sku, "Produit compté", "3710000000007", null).ConfigureAwait(false);
      var productId = await conn.ExecuteScalarAsync<Guid>(
        @"SELECT ""Id"" FROM ""Product"" WHERE ""ShopId""=@ShopId AND ""Sku""=@Sku LIMIT 1;",
        new { ShopId = shopId, Sku = sku }).ConfigureAwait(false);

      var sessionId = Guid.NewGuid();
      var now = DateTimeOffset.UtcNow;
      await conn.ExecuteAsync(
        @"INSERT INTO ""InventorySession"" (""Id"", ""Name"", ""StartedAtUtc"", ""CompletedAtUtc"")
          VALUES (@Id, @Name, @StartedAtUtc, @CompletedAtUtc);",
        new { Id = sessionId, Name = "Session comptée", StartedAtUtc = now, CompletedAtUtc = now }).ConfigureAwait(false);

      var runId = Guid.NewGuid();
      await conn.ExecuteAsync(
        @"INSERT INTO ""CountingRun"" (""Id"", ""InventorySessionId"", ""LocationId"", ""CountType"", ""StartedAtUtc"", ""CompletedAtUtc"", ""OperatorDisplayName"")
          VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAtUtc, @CompletedAtUtc, @Operator);",
        new
        {
          Id = runId,
          SessionId = sessionId,
          LocationId = locationId,
          CountType = (short)1,
          StartedAtUtc = now,
          CompletedAtUtc = now,
          Operator = "Tests"
        }).ConfigureAwait(false);

      await conn.ExecuteAsync(
        @"INSERT INTO ""CountLine"" (""Id"", ""CountingRunId"", ""ProductId"", ""Quantity"", ""CountedAtUtc"")
          VALUES (@Id, @RunId, @ProductId, @Quantity, @CountedAtUtc);",
        new
        {
          Id = Guid.NewGuid(),
          RunId = runId,
          ProductId = productId,
          Quantity = 2m,
          CountedAtUtc = now
        }).ConfigureAwait(false);
    }).ConfigureAwait(false);

    var response = await _f.Client.GetAsync($"/api/shops/{shopId}/products/count");
    response.EnsureSuccessStatusCode();

    using var payload = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var counted = payload.RootElement.GetProperty("countedReferences").GetInt64();
    Assert.True(counted >= 1);
  }
}
