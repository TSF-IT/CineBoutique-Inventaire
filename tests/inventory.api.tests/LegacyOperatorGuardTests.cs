using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class LegacyOperatorGuardTests : IntegrationTestBase
{
    public LegacyOperatorGuardTests(InventoryApiFixture fx)
    {
        UseFixture(fx);
    }

    [SkippableFact]
    public async Task StartRun_WithLegacyOperatorName_ReturnsBadRequest()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedMinimalInventoryAsync().ConfigureAwait(false);
        var client = CreateClient();

        var payload = new
        {
            shopId = seeded.ShopId,
            ownerUserId = seeded.OwnerUserId,
            countType = 1,
            operatorName = "legacy"
        };

        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            payload
        ).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        body.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetString().Should().NotBeNullOrWhiteSpace();
        error.GetString().Should().Contain("operatorName", "le message indique le champ legacy interdit");
    }

    private async Task<(Guid ShopId, Guid LocationId, Guid OwnerUserId)> SeedMinimalInventoryAsync()
    {
        var shopId = Guid.Empty;
        var locationId = Guid.Empty;
        var ownerUserId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Legacy").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "LEG-01", "Zone Legacy").ConfigureAwait(false);
            ownerUserId = await seeder.CreateShopUserAsync(shopId, "legacy-user", "Legacy Owner").ConfigureAwait(false);
        }).ConfigureAwait(false);

        return (shopId, locationId, ownerUserId);
    }
}
