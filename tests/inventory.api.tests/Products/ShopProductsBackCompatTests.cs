using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Products;

[Collection("api-tests")]
public sealed class ShopProductsBackCompatTests : IntegrationTestBase
{
    public ShopProductsBackCompatTests(InventoryApiFixture fx) => UseFixture(fx);

    [SkippableFact]
    public async Task ListProducts_FallsBack_WhenShopScopeIsMissing()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var sku = $"LEGACY-SKU-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        Guid shopId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.GetDefaultShopIdAsync().ConfigureAwait(false);
            await seeder.CreateProductAsync(shopId, sku, "Produit legacy").ConfigureAwait(false);
        }).ConfigureAwait(false);

        await using (var connection = await Fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            const string dropShopScopeSql = """
DO $$
BEGIN
    IF to_regclass('public."UX_Product_Shop_LowerSku"') IS NOT NULL THEN
        EXECUTE 'DROP INDEX "UX_Product_Shop_LowerSku"';
    END IF;
    IF to_regclass('public."UX_Product_Shop_Ean_NotNull"') IS NOT NULL THEN
        EXECUTE 'DROP INDEX "UX_Product_Shop_Ean_NotNull"';
    END IF;
    IF to_regclass('public."IX_Product_Shop_CodeDigits"') IS NOT NULL THEN
        EXECUTE 'DROP INDEX "IX_Product_Shop_CodeDigits"';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM information_schema.table_constraints
        WHERE table_schema = 'public'
          AND table_name = 'Product'
          AND constraint_name = 'FK_Product_Shop_ShopId'
    ) THEN
        EXECUTE 'ALTER TABLE "Product" DROP CONSTRAINT "FK_Product_Shop_ShopId"';
    END IF;
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'Product'
          AND column_name = 'ShopId'
    ) THEN
        EXECUTE 'ALTER TABLE "Product" DROP COLUMN "ShopId"';
    END IF;
END $$;
""";

            await using var command = new NpgsqlCommand(dropShopScopeSql, connection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var client = CreateClient();
        var response = await client.GetAsync(
            client.CreateRelativeUri($"/api/shops/{shopId}/products?page=1&pageSize=10&sortBy=sku")
        ).ConfigureAwait(false);

        await response.ShouldBeAsync(HttpStatusCode.OK, "la liste des produits doit rester accessible sans colonne ShopId").ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<ProductListResponse>().ConfigureAwait(false);

        payload.Should().NotBeNull();
        payload!.Items.Should().NotBeNull();
        payload.Items.Should().NotBeEmpty();
        payload.Items.Should().Contain(item => string.Equals(item.Sku, sku, StringComparison.Ordinal));
    }

    private sealed class ProductListResponse
    {
        public List<ProductItem> Items { get; set; } = new();
    }

    private sealed class ProductItem
    {
        public string Sku { get; set; } = string.Empty;
    }
}
