using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Fixtures;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    // La fixture est fournie par chaque classe dérivée via UseFixture(...)
    protected InventoryApiFixture Fixture { get; private set; } = default!;

    // A appeler dans le ctor de CHAQUE classe de tests dérivée
    protected void UseFixture(InventoryApiFixture fixture) => Fixture = fixture;

    protected HttpClient CreateClient() => Fixture.CreateClient();

    public async Task InitializeAsync()
    {
        // Si Docker indisponible: on sort proprement
        if (Fixture is null || !Fixture.IsDockerAvailable)
            return;

        // S’assure que la fixture est initialisée (DB, migrations, factory, client prêt)
        await Fixture.EnsureReadyAsync().ConfigureAwait(false);

        // Reset complet avant chaque classe de tests
        await Fixture.DbResetAsync().ConfigureAwait(false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected void SkipIfDockerUnavailable()
    {
        var reason = Fixture?.SkipReason ?? "Tests d'intégration ignorés : Docker est indisponible.";
        Skip.If(Fixture is null || !Fixture.IsDockerAvailable, reason);
    }
}
