using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class SoftOperatorMiddlewareTests : IntegrationTestBase
{
    public SoftOperatorMiddlewareTests(InventoryApiFixture fx)
    {
        UseFixture(fx);
    }

    [SkippableFact]
    public async Task GetHealth_IgnoresLegacyOperatorGuard()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var res = await client.GetAsync(client.CreateRelativeUri("/api/health")).ConfigureAwait(false);
        await res.ShouldBeAsync(HttpStatusCode.OK,
            "endpoint de lecture non filtré par le guard (GET mappé sur /api/health)");

    }

    [SkippableFact]
    public async Task Head_Health_IsNotMapped_Returns405_FromRouting()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(),
            "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var res = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, client.CreateRelativeUri("/api/health"))
        ).ConfigureAwait(false);

        await res.ShouldBeAsync(HttpStatusCode.MethodNotAllowed,
            "HEAD n'est pas mappé sur /api/health (comportement de routing, pas du guard)");
    }

    [SkippableFact]
    public async Task StartRun_WithLegacyOperatorName_ReturnsProblemDetails()
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

        await response.ShouldBeAsync(HttpStatusCode.BadRequest, "le middleware doit rejeter le champ operatorName");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>().ConfigureAwait(false);
        problem.Should().NotBeNull("le middleware retourne un ProblemDetails explicite");
        problem!.Title.Should().Be("Legacy field not allowed");
        problem.Detail.Should().Contain("operatorName");
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);

        var rawJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        root.TryGetProperty("detail", out var detail).Should().BeTrue();
        detail.GetString().Should().Contain("operatorName");
    }

    [SkippableFact]
    public async Task StartRun_WithOwnerUserId_Succeeds()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        var seeded = await SeedMinimalInventoryAsync().ConfigureAwait(false);
        var client = CreateClient();

        var request = new StartRunRequest(seeded.ShopId, seeded.OwnerUserId, 1);
        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/inventories/{seeded.LocationId}/start"),
            request
        ).ConfigureAwait(false);

        await response.ShouldBeAsync(HttpStatusCode.OK, "ownerUserId est supporté");

        var started = await response.Content.ReadFromJsonAsync<StartInventoryRunResponse>().ConfigureAwait(false);
        started.Should().NotBeNull();
        started!.OwnerUserId.Should().Be(seeded.OwnerUserId);
        started.LocationId.Should().Be(seeded.LocationId);
    }

    private async Task<(Guid ShopId, Guid LocationId, Guid OwnerUserId)> SeedMinimalInventoryAsync()
    {
        var shopId = Guid.Empty;
        var locationId = Guid.Empty;
        var ownerUserId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Soft").ConfigureAwait(false);
            locationId = await seeder.CreateLocationAsync(shopId, "SOFT-LOC", "Zone Soft").ConfigureAwait(false);
            ownerUserId = await seeder.CreateShopUserAsync(shopId, "soft-owner", "Soft Owner").ConfigureAwait(false);
        }).ConfigureAwait(false);

        return (shopId, locationId, ownerUserId);
    }
}
