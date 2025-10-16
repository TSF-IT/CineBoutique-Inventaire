using System.Threading;
using System.Threading.Tasks;

namespace CineBoutique.Inventory.Api.Services.Products;

public interface IProductLookupService
{
    Task<ProductLookupResult> ResolveAsync(string code, CancellationToken cancellationToken);
}
