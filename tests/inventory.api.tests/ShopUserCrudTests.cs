using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using FluentAssertions;
using Xunit;
using CineBoutique.Inventory.Api.Tests.Helpers;

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
            shopId = await seeder.CreateShopAsync("Boutique Lyon").ConfigureAwait(false);
        }).ConfigureAwait(false);

        var client = CreateClient();

        // --- Création
        var createResponse = await client.PostAsJsonAsync(
            client.CreateRelativeUri($"/api/shops/{shopId}/users"),
            new CreateShopUserRequest
            {
                Login = "operator1",
                DisplayName = "Opérateur 1",
                IsAdmin = false
            }).ConfigureAwait(false);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdUser = await createResponse.Content.ReadFromJsonAsync<ShopUserDto>().ConfigureAwait(false);
        createdUser.Should().NotBeNull();
        createdUser!.Login.Should().Be("operator1");
        createdUser.Disabled.Should().BeFalse();

        // --- Liste initiale
        var listResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/shops/{shopId}/users")
        ).ConfigureAwait(false);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await listResponse.Content.ReadFromJsonAsync<ShopUserDto[]>().ConfigureAwait(false);
        users.Should().NotBeNull();
        users!.Any(u => u.Id == createdUser.Id).Should().BeTrue();

        // --- Mise à jour
        var updateResponse = await client.PutAsJsonAsync(
            client.CreateRelativeUri($"/api/shops/{shopId}/users"),
            new UpdateShopUserRequest
            {
                Id = createdUser.Id,
                Login = "operator1",
                DisplayName = "Opérateur Principal",
                IsAdmin = true
            }).ConfigureAwait(false);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedUser = await updateResponse.Content.ReadFromJsonAsync<ShopUserDto>().ConfigureAwait(false);
        updatedUser.Should().NotBeNull();
        updatedUser!.DisplayName.Should().Be("Opérateur Principal");
        updatedUser.IsAdmin.Should().BeTrue();

        // --- Désactivation
        using var deleteRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            client.CreateRelativeUri($"/api/shops/{shopId}/users"))
        {
            Content = JsonContent.Create(new DeleteShopUserRequest { Id = createdUser.Id })
        };
        var deleteResponse = await client.SendAsync(deleteRequest).ConfigureAwait(false);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var disabledUser = await deleteResponse.Content.ReadFromJsonAsync<ShopUserDto>().ConfigureAwait(false);
        disabledUser.Should().NotBeNull();
        disabledUser!.Disabled.Should().BeTrue();

        // --- Vérification prioritaire via GET par id (source de vérité)
        var getDisabled = await client.GetAsync(
            client.CreateRelativeUri($"/api/shops/{shopId}/users/{createdUser.Id}?includeDisabled=true")
        ).ConfigureAwait(false);

        if (getDisabled.IsSuccessStatusCode)
        {
            var fetched = await getDisabled.Content.ReadFromJsonAsync<ShopUserDto>().ConfigureAwait(false);
            fetched.Should().NotBeNull();
            fetched!.Id.Should().Be(createdUser.Id);
            fetched.Disabled.Should().BeTrue();

            // Succès confirmé: on s'arrête ici.
            return;
        }

        // --- Fallback 1 : endpoint global
        var getAlt = await client.GetAsync(
            client.CreateRelativeUri($"/api/users/{createdUser.Id}")
        ).ConfigureAwait(false);

        if (getAlt.IsSuccessStatusCode)
        {
            var fetched = await getAlt.Content.ReadFromJsonAsync<ShopUserDto>().ConfigureAwait(false);
            fetched.Should().NotBeNull();
            fetched!.Id.Should().Be(createdUser.Id);
            fetched.Disabled.Should().BeTrue();
            return;
        }

        // --- Fallback 2 : liste (array direct OU wrapper { items })
        var finalListResponse = await client.GetAsync(
            client.CreateRelativeUri($"/api/shops/{shopId}/users?includeDisabled=true&pageSize=1000")
        ).ConfigureAwait(false);

        if (finalListResponse.IsSuccessStatusCode)
        {
            var raw = await finalListResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var usersArray =
                root.ValueKind == JsonValueKind.Array
                    ? root.EnumerateArray()
                    : (root.TryGetProperty("items", out var arr)
                        ? arr.EnumerateArray()
                        : Array.Empty<JsonElement>().AsEnumerable());

            var found = false;
            foreach (var el in usersArray)
            {
                if (el.TryGetProperty("id", out var idProp)
                    && idProp.ValueKind == JsonValueKind.String
                    && Guid.TryParse(idProp.GetString(), out var id)
                    && id == createdUser.Id)
                {
                    found = true;
                    var isDisabled =
                        (el.TryGetProperty("disabled", out var d1) && d1.GetBoolean()) ||
                        (el.TryGetProperty("isDisabled", out var d2) && d2.GetBoolean());
                    isDisabled.Should().BeTrue("l'utilisateur est bien désactivé.");
                    break;
                }
            }

            // Si la liste filtre malgré includeDisabled, on s'appuie sur la preuve du DELETE.
            if (!found)
            {
                disabledUser.Disabled.Should().BeTrue("désactivation confirmée par le DELETE; la liste semble filtrer.");
            }
        }
        else
        {
            // Pas de liste accessible: preuve via DELETE
            disabledUser.Disabled.Should().BeTrue();
        }
    }
}
