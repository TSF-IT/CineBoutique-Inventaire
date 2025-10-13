using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class AuthorizationTests : IntegrationTestBase
{
    public AuthorizationTests(InventoryApiFixture fx)
    {
        UseFixture(fx);
    }

    [SkippableFact]
    public async Task CreateShop_WithoutToken_Returns401()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/shops"),
            new CreateShopRequest { Name = "Boutique Auth" }
        ).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task CreateShop_WithOperatorToken_Returns403()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestTokenFactory.Create("operator"));

        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/shops"),
            new CreateShopRequest { Name = "Boutique Auth" }
        ).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task StartRun_WithoutToken_Returns401()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var context = await SeedInventoryAsync().ConfigureAwait(false);
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{context.LocationId}/start"),
            new StartRunRequest(context.ShopId, context.OperatorId, 1)
        ).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task StartRun_WithViewerToken_Returns403()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var context = await SeedInventoryAsync().ConfigureAwait(false);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestTokenFactory.Create("viewer"));

        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{context.LocationId}/start"),
            new StartRunRequest(context.ShopId, context.OperatorId, 1)
        ).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(Guid ShopId, Guid LocationId, Guid OperatorId)> SeedInventoryAsync()
    {
        var shopId = Guid.Empty;
        var locationId = Guid.Empty;
        var operatorId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Autorisation").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "AUTH-LOC", "Zone Autorisée").ConfigureAwait(false);
            operatorId = await seeder.CreateShopUserAsync(shopId, "operator", "Operator").ConfigureAwait(false);
        }).ConfigureAwait(false);

        return (shopId, locationId, operatorId);
    }
}
