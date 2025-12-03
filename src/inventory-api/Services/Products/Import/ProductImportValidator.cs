namespace CineBoutique.Inventory.Api.Services.Products.Import;

internal sealed class ProductImportValidator : IProductImportValidator
{
    public ProductImportValidationResult Validate(ProductCsvParseOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Errors.Count > 0)
        {
            return ProductImportValidationResult.Failure(outcome.Errors);
        }

        return ProductImportValidationResult.Success;
    }
}
