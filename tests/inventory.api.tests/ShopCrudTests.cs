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
public sealed class ShopCrudTests : IntegrationTestBase
{
    public ShopCrudTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task ShopCrudEndpointsManageShopLifecycle()
    {
        SkipIfDockerUnavailable();

        var client = CreateClient();

        // Create
        var createResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri("/api/shops"),
            new CreateShopRequest { Name = "Boutique Marseille" }
        ).ConfigureAwait(false);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdShop = await createResponse.Content.ReadFromJsonAsync<ShopDto>().ConfigureAwait(false);
        createdShop.Should().NotBeNull();
        createdShop!.Name.Should().Be("Boutique Marseille");

        // List
        var listResponse = await client.GetAsync(client.CreateRelativeUri("/api/shops")).ConfigureAwait(false);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var shops = await listResponse.Content.ReadFromJsonAsync<ShopDto[]>().ConfigureAwait(false);
        shops.Should().NotBeNull();
        shops!.Should().ContainSingle(shop => shop.Id == createdShop.Id);

        // Update
        var updateResponse = await client.PutAsJsonAsync(
            client.CreateRelativeUri("/api/shops"),
            new UpdateShopRequest { Id = createdShop.Id, Name = "Boutique Marseille - Renommée" }
        ).ConfigureAwait(false);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedShop = await updateResponse.Content.ReadFromJsonAsync<ShopDto>().ConfigureAwait(false);
        updatedShop.Should().NotBeNull();
        updatedShop!.Name.Should().Be("Boutique Marseille - Renommée");

        // Delete
        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            client.CreateRelativeUri("/api/shops"))
        {
            Content = JsonContent.Create(new DeleteShopRequest { Id = createdShop.Id })
        };

        var deleteResponse = await client.SendAsync(deleteRequest).ConfigureAwait(false);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Final list (shop doit avoir disparu)
        var finalListResponse = await client.GetAsync(client.CreateRelativeUri("/api/shops")).ConfigureAwait(false);
        finalListResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var finalShops = await finalListResponse.Content.ReadFromJsonAsync<ShopDto[]>().ConfigureAwait(false);
        finalShops.Should().NotBeNull();
        finalShops!.Should().NotContain(shop => shop.Id == createdShop.Id);
    }
}
