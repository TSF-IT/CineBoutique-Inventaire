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
public sealed class InventorySummaryEndpointTests : InventoryApiTestBase
{
    public InventorySummaryEndpointTests(PostgresTestContainerFixture postgres)
        : base(postgres)
    {
    }

    [Fact]
    public async Task GetInventorySummary_ReturnsEmptySummary_WhenNoData()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Summary-empty")).ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/inventories/summary?shopId={shop.Id:D}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.ActiveSessions);
        Assert.Equal(0, payload.OpenRuns);
        Assert.Equal(0, payload.CompletedRuns);
        Assert.Equal(0, payload.Conflicts);
        Assert.Null(payload.LastActivityUtc);
        Assert.Empty(payload.OpenRunDetails);
        Assert.Empty(payload.CompletedRunDetails);
        Assert.Empty(payload.ConflictZones);
    }

    [Fact]
    public async Task GetInventorySummary_ReportsActiveRunWithOwner()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var shop = await Data.CreateShopAsync(builder => builder.WithName("Summary-activity")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(
            shop,
            builder => builder.WithCode("Z1").WithLabel("Zone 1")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(
            shop,
            builder => builder.WithDisplayName("Camille").WithLogin("camille"))
            .ConfigureAwait(false);
        var session = await Data.CreateInventorySessionAsync(
            builder => builder.StartedAt(now.AddMinutes(-5)))
            .ConfigureAwait(false);
        var run = await Data.CreateCountingRunAsync(
            session,
            location,
            builder => builder
                .WithCountType(1)
                .StartedAt(now.AddMinutes(-5))
                .WithOwner(user.Id)
                .WithOperatorDisplayName("Camille"))
            .ConfigureAwait(false);
        var product = await Data.CreateProductAsync(
            builder => builder
                .WithSku("SKU-1")
                .WithName("Produit")
                .CreatedAt(now.AddDays(-1)))
            .ConfigureAwait(false);
        await Data.CreateCountLineAsync(
                run,
                product,
                builder => builder
                    .WithQuantity(1m)
                    .CountedAt(now.AddMinutes(-1)))
            .ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/inventories/summary?shopId={shop.Id:D}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.ActiveSessions);
        Assert.Equal(1, payload.OpenRuns);
        Assert.Equal(0, payload.CompletedRuns);
        Assert.Equal(0, payload.Conflicts);
        Assert.NotNull(payload.LastActivityUtc);
        Assert.True(payload.LastActivityUtc >= now.AddMinutes(-2));
        var openRun = Assert.Single(payload.OpenRunDetails);
        Assert.Equal(run.Id, openRun.RunId);
        Assert.Equal(location.Id, openRun.LocationId);
        Assert.Equal("Z1", openRun.LocationCode);
        Assert.Equal("Zone 1", openRun.LocationLabel);
        Assert.Equal(1, openRun.CountType);
        Assert.Equal(user.Id, openRun.OwnerUserId);
        Assert.Equal("Camille", openRun.OwnerDisplayName);
        Assert.Equal(run.StartedAtUtc.ToUniversalTime(), openRun.StartedAtUtc.ToUniversalTime());
    }

    [Fact]
    public async Task GetInventorySummary_ListsCompletedRuns()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var completedAt = startedAt.AddMinutes(45);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Summary-completed")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(
            shop,
            builder => builder.WithCode("ZC1").WithLabel("Zone C1")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(
            shop,
            builder => builder.WithDisplayName("Chloé").WithLogin("chloe"))
            .ConfigureAwait(false);
        var session = await Data.CreateInventorySessionAsync(
            builder => builder
                .WithName("Session zone C1")
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
                .WithOwner(user.Id)
                .WithOperatorDisplayName("Chloé"))
            .ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/inventories/summary?shopId={shop.Id:D}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.OpenRuns);
        Assert.Equal(1, payload.CompletedRuns);
        Assert.Single(payload.CompletedRunDetails);
        var completedRun = payload.CompletedRunDetails.Single();
        Assert.Equal(run.Id, completedRun.RunId);
        Assert.Equal(location.Id, completedRun.LocationId);
        Assert.Equal("ZC1", completedRun.LocationCode);
        Assert.Equal("Zone C1", completedRun.LocationLabel);
        Assert.Equal(1, completedRun.CountType);
        Assert.Equal(user.Id, completedRun.OwnerUserId);
        Assert.Equal("Chloé", completedRun.OwnerDisplayName);
        Assert.Equal(completedAt.ToUniversalTime(), completedRun.CompletedAtUtc.ToUniversalTime());
    }

    [Fact]
    public async Task GetInventorySummary_CountsConflictZones()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var shop = await Data.CreateShopAsync(builder => builder.WithName("Summary-conflicts")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(
            shop,
            builder => builder.WithCode("ZX1").WithLabel("Zone X1")).ConfigureAwait(false);
        var session = await Data.CreateInventorySessionAsync(
            builder => builder.StartedAt(now.AddHours(-1)).CompletedAt(now.AddMinutes(-30)))
            .ConfigureAwait(false);
        var run = await Data.CreateCountingRunAsync(
            session,
            location,
            builder => builder
                .WithCountType(1)
                .StartedAt(now.AddHours(-1))
                .CompletedAt(now.AddMinutes(-30)))
            .ConfigureAwait(false);
        var product = await Data.CreateProductAsync(
            builder => builder
                .WithSku("SKU-CONF")
                .WithName("Produit en conflit")
                .WithEan("98765432"))
            .ConfigureAwait(false);
        var line = await Data.CreateCountLineAsync(
            run,
            product,
            builder => builder
                .WithQuantity(10m)
                .CountedAt(now.AddMinutes(-30)))
            .ConfigureAwait(false);
        await Data.CreateConflictAsync(
            line,
            builder => builder
                .WithStatus("open")
                .CreatedAt(now.AddMinutes(-25)))
            .ConfigureAwait(false);

        var response = await Client.GetAsync($"/api/inventories/summary?shopId={shop.Id:D}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventorySummaryDto>().ConfigureAwait(false);
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Conflicts);
        var conflictZone = Assert.Single(payload.ConflictZones);
        Assert.Equal(location.Id, conflictZone.LocationId);
        Assert.Equal("ZX1", conflictZone.LocationCode);
        Assert.Equal("Zone X1", conflictZone.LocationLabel);
        Assert.Equal(1, conflictZone.ConflictLines);
    }
}
