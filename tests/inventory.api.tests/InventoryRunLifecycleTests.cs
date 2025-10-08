using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection(TestCollections.Postgres)]
public sealed class InventoryRunLifecycleTests : InventoryApiTestBase
{
    public InventoryRunLifecycleTests(PostgresTestContainerFixture postgres)
        : base(postgres)
    {
    }

    [Fact]
    public async Task StartInventoryRun_CreatesRunAndMarksLocationBusy()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Lifecycle-start")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("S1").WithLabel("Zone S1")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("amelie").WithDisplayName("Amélie"))
            .ConfigureAwait(false);

        var request = new StartRunRequest(shop.Id, user.Id, 1);
        var response = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/start", request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        Assert.NotNull(payload);
        Assert.Equal(location.Id, payload!.LocationId);
        Assert.Equal((short)1, payload.CountType);
        Assert.Equal(user.Id, payload.OwnerUserId);
        Assert.Equal("Amélie", payload.OwnerDisplayName);

        var locationsResponse = await Client.GetAsync($"/api/locations?shopId={shop.Id:D}").ConfigureAwait(false);
        locationsResponse.EnsureSuccessStatusCode();
        var locations = await locationsResponse.Content.ReadFromJsonAsync<List<LocationResponse>>().ConfigureAwait(false);
        Assert.NotNull(locations);
        var single = Assert.Single(locations!.Where(item => item.Id == location.Id));
        Assert.True(single.IsBusy);
        Assert.Equal(payload.RunId, single.ActiveRunId);
        Assert.Equal((short)1, single.ActiveCountType);
    }

    [Fact]
    public async Task StartInventoryRun_ReturnsConflict_WhenAnotherOperatorActive()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Lifecycle-conflict")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("S2").WithLabel("Zone S2")).ConfigureAwait(false);
        var firstUser = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("amelie").WithDisplayName("Amélie"))
            .ConfigureAwait(false);
        var secondUser = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("bruno").WithDisplayName("Bruno"))
            .ConfigureAwait(false);

        var startRequest = new StartRunRequest(shop.Id, firstUser.Id, 1);
        var firstResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/start", startRequest).ConfigureAwait(false);
        firstResponse.EnsureSuccessStatusCode();

        var conflictResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/start", new StartRunRequest(shop.Id, secondUser.Id, 1)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
    }

    [Fact]
    public async Task StartInventoryRun_ReturnsNotFound_WhenLocationBelongsToAnotherShop()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shopA = await Data.CreateShopAsync(builder => builder.WithName("Lifecycle-shopA")).ConfigureAwait(false);
        var shopB = await Data.CreateShopAsync(builder => builder.WithName("Lifecycle-shopB")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shopA, builder => builder.WithCode("S3").WithLabel("Zone S3")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shopB, builder => builder.WithLogin("lucie").WithDisplayName("Lucie"))
            .ConfigureAwait(false);

        var response = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/start", new StartRunRequest(shopB.Id, user.Id, 1)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseInventoryRun_RemovesActiveRun()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Lifecycle-release")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("S4").WithLabel("Zone S4")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("diane").WithDisplayName("Diane"))
            .ConfigureAwait(false);

        var startResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/start", new StartRunRequest(shop.Id, user.Id, 1)).ConfigureAwait(false);
        startResponse.EnsureSuccessStatusCode();
        var started = await startResponse.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        Assert.NotNull(started);

        var releaseResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/release", new ReleaseRunRequest(started!.RunId, user.Id)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, releaseResponse.StatusCode);

        var locationsResponse = await Client.GetAsync($"/api/locations?shopId={shop.Id:D}").ConfigureAwait(false);
        locationsResponse.EnsureSuccessStatusCode();
        var locations = await locationsResponse.Content.ReadFromJsonAsync<List<LocationResponse>>().ConfigureAwait(false);
        Assert.NotNull(locations);
        var single = Assert.Single(locations!.Where(item => item.Id == location.Id));
        Assert.False(single.IsBusy);
        Assert.Null(single.ActiveRunId);
    }

    [Fact]
    public async Task RestartInventoryRun_ClosesActiveRuns()
    {
        await ResetDatabaseAsync().ConfigureAwait(false);

        var shop = await Data.CreateShopAsync(builder => builder.WithName("Lifecycle-restart")).ConfigureAwait(false);
        var location = await Data.CreateLocationAsync(shop, builder => builder.WithCode("S5").WithLabel("Zone S5")).ConfigureAwait(false);
        var user = await Data.CreateShopUserAsync(shop, builder => builder.WithLogin("eric").WithDisplayName("Éric"))
            .ConfigureAwait(false);

        var startResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/start", new StartRunRequest(shop.Id, user.Id, 2)).ConfigureAwait(false);
        startResponse.EnsureSuccessStatusCode();

        var restartResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/restart", new RestartRunRequest(user.Id, 2)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, restartResponse.StatusCode);

        // Après restart, un nouveau start doit être possible immédiatement.
        var newStartResponse = await Client.PostAsJsonAsync($"/api/inventories/{location.Id}/start", new StartRunRequest(shop.Id, user.Id, 2)).ConfigureAwait(false);
        newStartResponse.EnsureSuccessStatusCode();
    }

    private sealed record LocationResponse
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public bool IsBusy { get; init; }
        public Guid? ActiveRunId { get; init; }
        public short? ActiveCountType { get; init; }
        public string? BusyBy { get; init; }
    }
}
