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

        if (!fixture.IsDatabaseAvailable && !string.IsNullOrWhiteSpace(fixture.SkipReason))
        {
            Console.WriteLine($"[tests] Integration backend indisponible: {fixture.SkipReason}");
        }
        
        return fixture.IsDatabaseAvailable;
    }
}
