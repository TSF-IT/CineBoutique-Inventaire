namespace CineBoutique.Inventory.Api.Configuration;

public sealed class AppSettingsOptions
{
    public bool CatalogEndpointsPublic { get; init; }
    public bool AdminEndpointsPublic { get; init; }
    public bool SeedOnStartup { get; set; }
}
