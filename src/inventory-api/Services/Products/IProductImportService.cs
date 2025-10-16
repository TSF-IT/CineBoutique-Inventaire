using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;

namespace CineBoutique.Inventory.Api.Services.Products;

public interface IProductImportService
{
    Task<ProductImportResponse> ImportAsync(Stream csvStream, CancellationToken cancellationToken);
}
