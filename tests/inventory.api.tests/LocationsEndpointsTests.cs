using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Infra;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class LocationsEndpointsTests : IntegrationTestBase
{
    public LocationsEndpointsTests(InventoryApiFixture fx)
    {
        UseFixture(fx);
    }

    [SkippableFact]
    public async Task CreateAndUpdateLocation_Workflow()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Zones").ConfigureAwait(false);
            await seeder.CreateLocationAsync(shopId, "EXIST", "Zone existante").ConfigureAwait(false);
        }).ConfigureAwait(false);

        Fixture.ClearAuditLogs();

        var client = CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/locations?shopId={shopId}"),
            new CreateLocationRequest
            {
                Code = "NOUV",
                Label = "Zone nouvelle"
            }).ConfigureAwait(false);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<LocationListItemDto>().ConfigureAwait(false);
        created.Should().NotBeNull();
        created!.Code.Should().Be("NOUV");
        created.Label.Should().Be("Zone nouvelle");
        created.CountStatuses.Should().NotBeNull();

        var listResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/locations?shopId={shopId}"))
            .ConfigureAwait(false);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<LocationListItemDto[]>().ConfigureAwait(false);
        list.Should().NotBeNull();
        list!.Any(item => item.Id == created.Id).Should().BeTrue();

        var updateResponse = await client.PutAsJsonAsync(
            client.CreateRelativeUri($"/api/locations/{created.Id}?shopId={shopId}"),
            new UpdateLocationRequest
            {
                Code = "UPDT",
                Label = "Zone mise à jour"
            }).ConfigureAwait(false);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<LocationListItemDto>().ConfigureAwait(false);
        updated.Should().NotBeNull();
        updated!.Code.Should().Be("UPDT");
        updated.Label.Should().Be("Zone mise à jour");

        var auditEntries = Fixture.DrainAuditLogs();
        auditEntries.Should().NotBeNull();
        auditEntries.Should().Contain(entry => entry.Category == "locations.create.success");
        auditEntries.Should().Contain(entry => entry.Category == "locations.update.success");
    }

    [SkippableFact]
    public async Task UpdateLocation_NotFound_ReturnsProblem()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Introuvable").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();

        var response = await client.PutAsJsonAsync(
            client.CreateRelativeUri($"/api/locations/{Guid.NewGuid()}?shopId={shopId}"),
            new UpdateLocationRequest
            {
                Label = "Zone fantôme"
            }).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>().ConfigureAwait(false);
        problem.Should().NotBeNull();
        problem!.Detail.Should().Be("Impossible de trouver cette zone pour la boutique demandée.");
    }

    [SkippableFact]
    public async Task CreateLocation_DuplicateCode_ReturnsConflict()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "No Docker/Testcontainers and no TEST_DB_CONN provided.");

        Guid shopId = Guid.Empty;
        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Doublon Zone").ConfigureAwait(false);
            await seeder.CreateLocationAsync(shopId, "DUP", "Zone initiale").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/locations?shopId={shopId}"),
            new CreateLocationRequest
            {
                Code = "dup",
                Label = "Zone dupliquée"
            }).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>().ConfigureAwait(false);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Code déjà utilisé");
        problem.Detail.Should().Be("Impossible de créer cette zone : le code « DUP » est déjà attribué dans cette boutique.");
    }
}
