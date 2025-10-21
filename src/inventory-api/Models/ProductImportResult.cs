namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductImportResult(ProductImportResponse Response, ProductImportResultType ResultType);

public enum ProductImportResultType
{
    Succeeded,
    DryRun,
    ValidationFailed,
    Skipped
}
