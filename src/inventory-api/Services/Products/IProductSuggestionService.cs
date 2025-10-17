using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CineBoutique.Inventory.Api.Services.Products;

public interface IProductSuggestionService
{
    Task<IReadOnlyList<ProductSuggestionResultItem>> SuggestAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);
}
