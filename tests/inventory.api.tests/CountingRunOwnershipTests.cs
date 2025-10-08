using System;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class CountingRunOwnershipTests : InventoryApiTestBase
{
    public CountingRunOwnershipTests(PostgresTestContainerFixture postgres)
        : base(postgres)
    {
    }

    [Fact]
    public async Task InventorySummary_UsesOwnerDisplayName_WhenOwnerAssigned()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var shop = await Data.CreateShopAsync(builder => builder.WithName("Ownership-shop")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("Z1").WithLabel("Zone 1")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shop, builder => builder.WithDisplayName("Camille").WithLogin("camille"))
            .ConfigureAwait(false);
        var session = await Data.CreateInventorySessionAsync(builder => builder.StartedAt(now.AddHours(-1)).CompletedAt(now.AddMinutes(-10)))
            .ConfigureAwait(false);
        await Data.CreateCountingRunAsync(
            session,
            location,
            builder => builder
                .WithCountType(1)
                .StartedAt(now.AddHours(-1))
                .CompletedAt(now.AddMinutes(-10))
                .WithOwner(user.Id)
                .WithOperatorDisplayName("Camille"))
            .ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/inventories/summary?shopId={shop.Id:D}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        Assert.NotNull(payload);
        var completedRun = Assert.Single(payload!.CompletedRunDetails);
        Assert.Equal(user.Id, completedRun.OwnerUserId);
        Assert.Equal("Camille", completedRun.OwnerDisplayName);
    }

    [Fact]
    public async Task InventorySummary_FallsBackToOperator_WhenOwnerMissing()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var shop = await Data.CreateShopAsync(builder => builder.WithName("Ownership-fallback")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("Z2").WithLabel("Zone 2")).ConfigureAwait(false);
        var session = await Data.CreateInventorySessionAsync(builder => builder.StartedAt(now.AddHours(-1)).CompletedAt(now.AddMinutes(-5)))
            .ConfigureAwait(false);
        await Data.CreateCountingRunAsync(
            session,
            location,
            builder => builder
                .WithCountType(1)
                .StartedAt(now.AddHours(-1))
                .CompletedAt(now.AddMinutes(-5))
                .WithOperatorDisplayName("Équipe nuit"))
            .ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/inventories/summary?shopId={shop.Id:D}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        Assert.NotNull(payload);
        var completedRun = Assert.Single(payload!.CompletedRunDetails);
        Assert.Null(completedRun.OwnerUserId);
        Assert.Equal("Équipe nuit", completedRun.OwnerDisplayName);
    }
}
