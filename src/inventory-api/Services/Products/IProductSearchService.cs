using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CineBoutique.Inventory.Api.Services.Products;

public interface IProductSearchService
{
    Task<IReadOnlyList<ProductSearchResultItem>> SearchAsync(string code, int limit, CancellationToken cancellationToken);
}
