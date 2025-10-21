namespace CineBoutique.Inventory.Api.Models;

using System.IO;

public enum ProductImportMode
{
    ReplaceCatalogue
}

public sealed record ProductImportCommand(
    Stream CsvStream,
    bool DryRun,
    string? Username,
    Guid? ShopId = null,
    ProductImportMode Mode = ProductImportMode.ReplaceCatalogue);
