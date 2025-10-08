using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class ConflictZoneDetailEndpointTests : InventoryApiTestBase
{
    public ConflictZoneDetailEndpointTests(PostgresTestContainerFixture postgres)
        : base(postgres)
    {
    }

    [Fact]
    public async Task GetConflictZoneDetail_ReturnsNotFound_WhenLocationIsUnknown()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/conflicts/{Guid.NewGuid()}").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetConflictZoneDetail_ReturnsConflictItems()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var shop = await Data.CreateShopAsync(builder => builder.WithName("Conflicts-shop")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(
            shop,
            builder => builder.WithCode("B1").WithLabel("Zone B1")).ConfigureAwait(false);

        var session = await Data.CreateInventorySessionAsync(
            builder => builder
                .WithName("Session B1")
                .StartedAt(now.AddHours(-2))
                .CompletedAt(now.AddMinutes(-30)))
            .ConfigureAwait(false);

        var run1 = await Data.CreateCountingRunAsync(
            session,
            location,
            builder => builder
                .WithCountType(1)
                .StartedAt(now.AddHours(-2))
                .CompletedAt(now.AddMinutes(-40))
                .WithOperatorDisplayName("Alice"))
            .ConfigureAwait(false);

        var run2 = await Data.CreateCountingRunAsync(
            session,
            location,
            builder => builder
                .WithCountType(2)
                .StartedAt(now.AddHours(-1))
                .CompletedAt(now.AddMinutes(-20))
                .WithOperatorDisplayName("Bastien"))
            .ConfigureAwait(false);

        var product1 = await Data.CreateProductAsync(
            builder => builder
                .WithSku("SKU-1")
                .WithName("Produit 1")
                .WithEan("111"))
            .ConfigureAwait(false);
        var product2 = await Data.CreateProductAsync(
            builder => builder
                .WithSku("SKU-2")
                .WithName("Produit 2")
                .WithEan("222"))
            .ConfigureAwait(false);

        var run1Line1 = await Data.CreateCountLineAsync(
            run1,
            product1,
            builder => builder
                .WithQuantity(5)
                .CountedAt(run1.CompletedAtUtc!.Value))
            .ConfigureAwait(false);
        var run1Line2 = await Data.CreateCountLineAsync(
            run1,
            product2,
            builder => builder
                .WithQuantity(3)
                .CountedAt(run1.CompletedAtUtc!.Value))
            .ConfigureAwait(false);

        await Data.CreateCountLineAsync(
                run2,
                product1,
                builder => builder
                    .WithQuantity(8)
                    .CountedAt(run2.CompletedAtUtc!.Value))
            .ConfigureAwait(false);
        await Data.CreateCountLineAsync(
                run2,
                product2,
                builder => builder
                    .WithQuantity(1)
                    .CountedAt(run2.CompletedAtUtc!.Value))
            .ConfigureAwait(false);

        await Data.CreateConflictAsync(
                run1Line1,
                builder => builder
                    .WithStatus("open")
                    .CreatedAt(now.AddMinutes(-35)))
            .ConfigureAwait(false);
        await Data.CreateConflictAsync(
                run1Line2,
                builder => builder
                    .WithStatus("open")
                    .CreatedAt(now.AddMinutes(-35)))
            .ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/conflicts/{location.Id}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ConflictZoneDetailDto>().ConfigureAwait(false);
        Assert.NotNull(payload);
        Assert.Equal(location.Id, payload!.LocationId);
        Assert.Equal("B1", payload.LocationCode);
        Assert.Equal("Zone B1", payload.LocationLabel);

        Assert.Equal(2, payload.Runs.Count);
        var run1Header = Assert.Single(payload.Runs, run => run.RunId == run1.Id);
        Assert.Equal((short)1, run1Header.CountType);
        Assert.Equal(run1.CompletedAtUtc!.Value.ToUniversalTime(), run1Header.CompletedAtUtc.ToUniversalTime());
        Assert.Equal("Alice", run1Header.OwnerDisplayName);
        var run2Header = Assert.Single(payload.Runs, run => run.RunId == run2.Id);
        Assert.Equal((short)2, run2Header.CountType);
        Assert.Equal("Bastien", run2Header.OwnerDisplayName);

        Assert.Equal(2, payload.Items.Count);
        var item1 = Assert.Single(payload.Items, item => item.ProductId == product1.Id);
        Assert.Equal("111", item1.Ean);
        Assert.Equal("SKU-1", item1.Sku);
        Assert.Equal(5, item1.QtyC1);
        Assert.Equal(8, item1.QtyC2);
        Assert.Equal(-3, item1.Delta);
        Assert.Equal(2, item1.AllCounts.Count);

        var item2 = Assert.Single(payload.Items, item => item.ProductId == product2.Id);
        Assert.Equal("222", item2.Ean);
        Assert.Equal(3, item2.QtyC1);
        Assert.Equal(1, item2.QtyC2);
        Assert.Equal(2, item2.Delta);
    }
}
