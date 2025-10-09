using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests;

[Collection("api-tests")]
public sealed class HealthEndpointsTests : IntegrationTestBase
{
    public HealthEndpointsTests(InventoryApiFixture fx) { UseFixture(fx); }

    [SkippableFact]
    public async Task Health_Returns200_WithExpectedPayloadShape()
    {
        SkipIfDockerUnavailable();

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var response = await client.GetAsync(client.CreateRelativeUri("/api/health")).ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "health endpoint");

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.TryGetProperty("app", out var appProperty).Should().BeTrue();
        appProperty.GetString().Should().NotBeNullOrWhiteSpace();
        root.TryGetProperty("db", out var dbProperty).Should().BeTrue();
        dbProperty.GetString().Should().NotBeNullOrWhiteSpace();
        root.TryGetProperty("users", out var usersProperty).Should().BeTrue();
        usersProperty.ValueKind.Should().Be(JsonValueKind.Number);
        root.TryGetProperty("runsWithoutOwner", out var runsProperty).Should().BeTrue();
        runsProperty.ValueKind.Should().Be(JsonValueKind.Number);
    }

    [SkippableFact]
    public async Task Ready_Returns200()
    {
        SkipIfDockerUnavailable();

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);
        var client = CreateClient();

        var response = await client.GetAsync(client.CreateRelativeUri("/ready")).ConfigureAwait(false);
        await response.ShouldBeAsync(HttpStatusCode.OK, "ready endpoint");
    }
}
