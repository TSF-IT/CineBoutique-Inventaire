using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class ShopCrudTests : IntegrationTestBase, IAsyncLifetime
{
    public ShopCrudTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task ShopCrudEndpointsManageShopLifecycle()
    {
        SkipIfDockerUnavailable();

        var client = CreateClient();

        var createResponse = await client
            .PostAsJsonAsync(
                client.CreateRelativeUri("/api/shops"),
                new CreateShopRequest { Name = "Boutique Marseille" }).ConfigureAwait(true);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdShop = await createResponse.Content.ReadFromJsonAsync<ShopDto>().ConfigureAwait(true);
        createdShop.Should().NotBeNull();
        createdShop!.Name.Should().Be("Boutique Marseille");

        var listResponse = await client.GetAsync(client.CreateRelativeUri("/api/shops")).ConfigureAwait(true);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var shops = await listResponse.Content.ReadFromJsonAsync<ShopDto[]>().ConfigureAwait(true);
        shops.Should().NotBeNull();
        shops!.Should().ContainSingle(shop => shop.Id == createdShop.Id);

        var updateResponse = await client
            .PutAsJsonAsync(
                client.CreateRelativeUri("/api/shops"),
                new UpdateShopRequest
                {
                    Id = createdShop.Id,
                    Name = "Boutique Marseille - Renommée"
                }).ConfigureAwait(true);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedShop = await updateResponse.Content.ReadFromJsonAsync<ShopDto>().ConfigureAwait(true);
        updatedShop.Should().NotBeNull();
        updatedShop!.Name.Should().Be("Boutique Marseille - Renommée");

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, client.CreateRelativeUri("/api/shops"))
        {
            Content = JsonContent.Create(new DeleteShopRequest { Id = createdShop.Id })
        };
        var deleteResponse = await client.SendAsync(deleteRequest).ConfigureAwait(true);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var finalListResponse = await client.GetAsync(client.CreateRelativeUri("/api/shops")).ConfigureAwait(true);
        finalListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalShops = await finalListResponse.Content.ReadFromJsonAsync<ShopDto[]>().ConfigureAwait(true);
        finalShops.Should().NotBeNull();
        finalShops!.Should().NotContain(shop => shop.Id == createdShop.Id);
    }
}
