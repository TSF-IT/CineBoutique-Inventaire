using CineBoutique.Inventory.Api.Tests.Fixtures;

namespace CineBoutique.Inventory.Api.Tests.Infra;

public static class TestEnvironment
{
    public static bool IsIntegrationBackendAvailable()
    {
        if (TestDbOptions.UseExternalDb)
        {
            return true;
        }

        var fixture = PostgresContainerFixture.Instance;
        if (fixture is null)
        {
            return false;
        }

        return fixture.IsDatabaseAvailable;
    }
}
