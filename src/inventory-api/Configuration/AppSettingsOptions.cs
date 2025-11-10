namespace CineBoutique.Inventory.Api.Configuration;

public sealed class AppSettingsOptions
{
    public bool CatalogEndpointsPublic { get; init; } = false;
    public bool AdminEndpointsPublic { get; init; } = false;
    public bool SeedOnStartup { get; set; }
}
