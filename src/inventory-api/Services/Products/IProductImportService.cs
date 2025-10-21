using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;

namespace CineBoutique.Inventory.Api.Services.Products;

public interface IProductImportService
{
    Task<ProductImportResult> ImportAsync(ProductImportCommand command, CancellationToken cancellationToken);
}
