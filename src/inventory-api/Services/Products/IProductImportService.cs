using System.Threading;
using System.Threading.Tasks;

namespace CineBoutique.Inventory.Api.Services.Products;

public interface IProductImportService
{
    Task<ProductImportResult> ImportAsync(ProductImportCommand command, CancellationToken cancellationToken);
}
