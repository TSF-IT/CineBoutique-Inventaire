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

        var client = CreateClient();
        var shopId = await Fixture.Seeder.CreateShopAsync("Boutique Lyon").ConfigureAwait(true);

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
        users!.Should().ContainSingle(user => user.Id == createdUser.Id);

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
        finalUsers!.Single(user => user.Id == createdUser.Id).Disabled.Should().BeTrue();
    }
}
