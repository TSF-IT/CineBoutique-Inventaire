using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class ShopUserCrudTests : IntegrationTestBase
{
    public ShopUserCrudTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task ShopUserEndpointsManageUserLifecycle()
    {
        SkipIfDockerUnavailable();

        Guid shopId = Guid.Empty;

        await Fixture.ResetAndSeedAsync(async seeder =>
        {
            shopId = await seeder.CreateShopAsync("Boutique Lyon").ConfigureAwait(true);
        }).ConfigureAwait(true);

        var client = CreateClient();

        var createResponse = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri($"/api/shops/{shopId}/users"),
                new CreateShopUserRequest
                {
                    Login = "operator1",
                    DisplayName = "Opérateur 1",
                    IsAdmin = false
                }).ConfigureAwait(true);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdUser = await createResponse.Content.ReadFromJsonAsync<ShopUserDto>().ConfigureAwait(true);
        createdUser.Should().NotBeNull();
        createdUser!.Login.Should().Be("operator1");
        createdUser.Disabled.Should().BeFalse();

        var listResponse = await client.GetAsync(client.CreateRelativeUri($"/api/shops/{shopId}/users")).ConfigureAwait(true);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = await listResponse.Content.ReadFromJsonAsync<ShopUserDto[]>().ConfigureAwait(true);
        users.Should().NotBeNull();
        var createdListingEntry = users!.SingleOrDefault(user => user.Id == createdUser.Id);
        createdListingEntry.Should().NotBeNull(
            "l'utilisateur nouvellement créé doit être présent dans la liste retournée pour la boutique concernée");
        createdListingEntry!.Disabled.Should().BeFalse();

        var updateResponse = await client
            .PutAsJsonAsync(
                client.CreateRelativeUri($"/api/shops/{shopId}/users"),
                new UpdateShopUserRequest
                {
                    Id = createdUser.Id,
                    Login = "operator1",
                    DisplayName = "Opérateur Principal",
                    IsAdmin = true
                }).ConfigureAwait(true);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedUser = await updateResponse.Content.ReadFromJsonAsync<ShopUserDto>().ConfigureAwait(true);
        updatedUser.Should().NotBeNull();
        updatedUser!.DisplayName.Should().Be("Opérateur Principal");
        updatedUser.IsAdmin.Should().BeTrue();

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, client.CreateRelativeUri($"/api/shops/{shopId}/users"))
        {
            Content = JsonContent.Create(new DeleteShopUserRequest { Id = createdUser.Id })
        };
        var deleteResponse = await client.SendAsync(deleteRequest).ConfigureAwait(true);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var disabledUser = await deleteResponse.Content.ReadFromJsonAsync<ShopUserDto>().ConfigureAwait(true);
        disabledUser.Should().NotBeNull();
        disabledUser!.Disabled.Should().BeTrue();

        var finalListResponse = await client.GetAsync(client.CreateRelativeUri($"/api/shops/{shopId}/users")).ConfigureAwait(true);
        finalListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalUsers = await finalListResponse.Content.ReadFromJsonAsync<ShopUserDto[]>().ConfigureAwait(true);
        finalUsers.Should().NotBeNull();
        var disabledEntry = finalUsers!.SingleOrDefault(user => user.Id == createdUser.Id);
        disabledEntry.Should().NotBeNull(
            "l'utilisateur désactivé doit rester visible dans la liste finale pour permettre la vérification de son statut");
        disabledEntry!.Disabled.Should().BeTrue();
    }
}
