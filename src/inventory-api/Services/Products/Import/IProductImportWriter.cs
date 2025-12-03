using System.Data;
using CineBoutique.Inventory.Api.Models;

namespace CineBoutique.Inventory.Api.Services.Products.Import;

public interface IProductImportWriter
{
    Task<ProductImportWriteStatistics> PreviewAsync(
        IReadOnlyList<ProductCsvRow> rows,
        Guid shopId,
        IDbTransaction transaction,
        CancellationToken cancellationToken);

    Task<ProductImportWriteStatistics> WriteAsync(
        IReadOnlyList<ProductCsvRow> rows,
        Guid shopId,
        ProductImportMode mode,
        IDbTransaction transaction,
        CancellationToken cancellationToken);
}
