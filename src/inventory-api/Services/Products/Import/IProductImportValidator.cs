namespace CineBoutique.Inventory.Api.Services.Products.Import;

public interface IProductImportValidator
{
    ProductImportValidationResult Validate(ProductCsvParseOutcome outcome);
}
