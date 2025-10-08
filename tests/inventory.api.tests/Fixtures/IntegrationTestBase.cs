using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Fixtures;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected IntegrationTestBase(InventoryApiFixture fixture)
    {
        Fixture = fixture;
    }

    protected InventoryApiFixture Fixture { get; }

    protected HttpClient CreateClient() => Fixture.CreateClient();

    public async Task InitializeAsync()
    {
        if (!Fixture.IsDockerAvailable)
        {
            return;
        }

        await Fixture.ResetDatabaseAsync().ConfigureAwait(true);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected void SkipIfDockerUnavailable()
    {
        Skip.If(!Fixture.IsDockerAvailable, Fixture.SkipReason ?? "Tests d'intégration ignorés : Docker est indisponible.");
    }
}
