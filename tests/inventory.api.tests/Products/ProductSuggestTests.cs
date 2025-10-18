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
        // --- Helpers locaux d’Arrange (sans ON CONFLICT) ---
        static async System.Threading.Tasks.Task<long> UpsertGroupAsync(
            System.Data.IDbConnection c, string code, string label)
        {
            const string sql = @"
        WITH upsert AS (
          UPDATE ""ProductGroup""
          SET ""Label"" = @label
          WHERE ""Code"" = @code
          RETURNING ""Id""
        )
        INSERT INTO ""ProductGroup"" (""Code"",""Label"")
        SELECT @code, @label
        WHERE NOT EXISTS (SELECT 1 FROM upsert)
        RETURNING ""Id"";";
            return await Dapper.SqlMapper.ExecuteScalarAsync<long>(c, sql, new { code, label })
                .ConfigureAwait(false);
        }

        static async System.Threading.Tasks.Task UpsertProductAsync(
            System.Data.IDbConnection c, string sku, string name, string ean, long gid)
        {
            // Détecte dynamiquement la présence des colonnes de timestamps pour éviter 42703/23502
            const string q = @"
          select 1 from information_schema.columns
          where table_schema='public' and table_name='Product' and column_name=@col limit 1;";
            var hasCreated = await Dapper.SqlMapper.ExecuteScalarAsync<object>(c, q, new { col = "CreatedAtUtc" })
                                .ConfigureAwait(false) is not null;
            var hasUpdated = await Dapper.SqlMapper.ExecuteScalarAsync<object>(c, q, new { col = "UpdatedAtUtc" })
                                .ConfigureAwait(false) is not null;

            var updateSet = "\"Name\"=@name, \"Ean\"=@ean, \"GroupId\"=@gid"
                          + (hasUpdated ? ", \"UpdatedAtUtc\" = NOW() AT TIME ZONE 'UTC'" : "");
            var insertCols = new System.Collections.Generic.List<string> { "\"Sku\"", "\"Name\"", "\"Ean\"", "\"GroupId\"" };
            var insertVals = new System.Collections.Generic.List<string> { "@sku", "@name", "@ean", "@gid" };
            if (hasCreated) { insertCols.Add("\"CreatedAtUtc\""); insertVals.Add("NOW() AT TIME ZONE 'UTC'"); }
            if (hasUpdated) { insertCols.Add("\"UpdatedAtUtc\""); insertVals.Add("NOW() AT TIME ZONE 'UTC'"); }

            var sql = $@"
        WITH upsert AS (
          UPDATE ""Product""
          SET {updateSet}
          WHERE ""Sku"" = @sku
          RETURNING ""Sku""
        )
        INSERT INTO ""Product"" ({string.Join(", ", insertCols)})
        SELECT {string.Join(", ", insertVals)}
        WHERE NOT EXISTS (SELECT 1 FROM upsert);";

            await Dapper.SqlMapper.ExecuteAsync(c, sql, new { sku, name, ean, gid }).ConfigureAwait(false);
        }

        // --- Arrange concret (équivalent fonctionnel, sans ON CONFLICT) ---
        var gid = await UpsertGroupAsync(conn, "cafe", "Café").ConfigureAwait(false);
        await UpsertProductAsync(conn, "CB-0001", "Café Grains 1kg", "321000000001", gid).ConfigureAwait(false);
        await UpsertProductAsync(conn, "CB-0002", "Café Moulu",        "321000000002", gid).ConfigureAwait(false);
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
