namespace CineBoutique.Inventory.Api.Services.Products.Import;

public interface IProductImportReader
{
    Task<ProductImportBuffer> BufferAsync(Stream source, CancellationToken cancellationToken);

    Task<ProductCsvParseOutcome> ParseAsync(ProductImportBuffer buffer, CancellationToken cancellationToken);
}
