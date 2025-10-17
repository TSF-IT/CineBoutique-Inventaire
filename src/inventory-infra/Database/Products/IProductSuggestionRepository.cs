using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CineBoutique.Inventory.Infrastructure.Database.Products;

public interface IProductSuggestionRepository
{
    Task<IReadOnlyList<ProductSuggestionItem>> SuggestAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);
}
