using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CineBoutique.Inventory.Infrastructure.Database.Products;

public interface IProductLookupRepository
{
    Task<ProductLookupItem?> FindBySkuAsync(string sku, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductLookupItem>> FindByRawCodeAsync(string rawCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductLookupItem>> FindByCodeDigitsAsync(string digits, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductLookupItem>> SearchProductsAsync(
        string code,
        int limit,
        bool hasPaging,
        int pageSize,
        int offset,
        CancellationToken cancellationToken);
}
