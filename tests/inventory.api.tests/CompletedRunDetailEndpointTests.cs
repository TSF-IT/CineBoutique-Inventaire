using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class CompletedRunDetailEndpointTests : InventoryApiTestBase
{
    public CompletedRunDetailEndpointTests(PostgresTestContainerFixture postgres)
        : base(postgres)
    {
    }

    [Fact]
    public async Task GetCompletedRunDetail_ReturnsNotFound_WhenRunDoesNotExist()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/inventories/runs/{Guid.NewGuid()}").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCompletedRunDetail_ReturnsNotFound_WhenRunNotCompleted()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var shop = await Data.CreateShopAsync().ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(
            shop,
            builder => builder.WithCode("A1").WithLabel("Zone A1")).ConfigureAwait(false);
        var session = await Data.CreateInventorySessionAsync(
            builder => builder.StartedAt(now.AddMinutes(-10))).ConfigureAwait(false);

        var run = await Data.CreateCountingRunAsync(
            session,
            location,
            builder => builder
                .WithCountType(1)
                .StartedAt(now.AddMinutes(-10))
                .WithOperatorDisplayName("Alice"))
            .ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/inventories/runs/{run.Id}").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCompletedRunDetail_ReturnsLines_WhenRunExists()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var completedAt = startedAt.AddMinutes(12);

        var shop = await Data.CreateShopAsync().ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(
            shop,
            builder => builder.WithCode("B1").WithLabel("Zone B1")).ConfigureAwait(false);
        var session = await Data.CreateInventorySessionAsync(
            builder => builder
                .WithName("Session zone B1")
                .StartedAt(startedAt)
                .CompletedAt(completedAt))
            .ConfigureAwait(false);

        var run = await Data.CreateCountingRunAsync(
            session,
            location,
            builder => builder
                .WithCountType(1)
                .StartedAt(startedAt)
                .CompletedAt(completedAt)
                .WithOperatorDisplayName("Bastien"))
            .ConfigureAwait(false);

        var product1 = await Data.CreateProductAsync(
            builder => builder
                .WithSku("SKU-1")
                .WithName("Produit 1")
                .WithEan("321")
                .CreatedAt(startedAt))
            .ConfigureAwait(false);

        var product2 = await Data.CreateProductAsync(
            builder => builder
                .WithSku("SKU-2")
                .WithName("Produit 2")
                .WithEan("654")
                .CreatedAt(startedAt))
            .ConfigureAwait(false);

        await Data.CreateCountLineAsync(
                run,
                product1,
                builder => builder
                    .WithQuantity(5.5m)
                    .CountedAt(completedAt))
            .ConfigureAwait(false);

        await Data.CreateCountLineAsync(
                run,
                product2,
                builder => builder
                    .WithQuantity(3m)
                    .CountedAt(completedAt))
            .ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/inventories/runs/{run.Id}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CompletedRunDetailDto>().ConfigureAwait(false);
        Assert.NotNull(payload);

        Assert.Equal(run.Id, payload!.RunId);
        Assert.Equal(location.Id, payload.LocationId);
        Assert.Equal("B1", payload.LocationCode);
        Assert.Equal("Zone B1", payload.LocationLabel);
        Assert.Equal((short)1, payload.CountType);
        Assert.Equal("Bastien", payload.OperatorDisplayName);
        Assert.Equal(startedAt.ToUniversalTime(), payload.StartedAtUtc.ToUniversalTime());
        Assert.Equal(completedAt.ToUniversalTime(), payload.CompletedAtUtc.ToUniversalTime());
        Assert.Equal(2, payload.Items.Count);

        var first = Assert.Single(payload.Items, item => item.ProductId == product1.Id);
        Assert.Equal("SKU-1", first.Sku);
        Assert.Equal("Produit 1", first.Name);
        Assert.Equal("321", first.Ean);
        Assert.Equal(5.5m, first.Quantity);

        var second = Assert.Single(payload.Items, item => item.ProductId == product2.Id);
        Assert.Equal("SKU-2", second.Sku);
        Assert.Equal("Produit 2", second.Name);
        Assert.Equal("654", second.Ean);
        Assert.Equal(3m, second.Quantity);
    }
}
