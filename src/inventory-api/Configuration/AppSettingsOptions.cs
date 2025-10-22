namespace CineBoutique.Inventory.Api.Configuration;

public sealed class AppSettingsOptions
{
    public bool CatalogEndpointsPublic { get; init; } = true;
    public bool SeedOnStartup { get; set; }
}
