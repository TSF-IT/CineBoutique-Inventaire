using CineBoutique.Inventory.Api.Models;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed record ProductImportResult(ProductImportResponse Response, ProductImportResultType ResultType);

public enum ProductImportResultType
{
    Succeeded,
    DryRun,
    ValidationFailed,
    Skipped
}
