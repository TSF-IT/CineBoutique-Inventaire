namespace CineBoutique.Inventory.Api.Tests.Infra;

public static class TestDbOptions
{
    public static string? ExternalConnectionString =>
        Environment.GetEnvironmentVariable("TEST_DB_CONN");

    public static bool UseExternalDb =>
        !string.IsNullOrWhiteSpace(ExternalConnectionString);
}
