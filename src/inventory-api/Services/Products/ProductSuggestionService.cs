using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database.Products;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductSuggestionService : IProductSuggestionService
{
    private readonly IProductSuggestionRepository _repository;

    public ProductSuggestionService(IProductSuggestionRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<ProductSuggestionResultItem>> SuggestAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var sanitizedQuery = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sanitizedQuery))
        {
            throw new ArgumentException("Le paramètre de recherche est requis.", nameof(query));
        }

        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Le nombre de résultats doit être positif.");
        }

        var effectiveLimit = Math.Clamp(limit, 1, 50);
        var rows = await _repository
            .SuggestAsync(sanitizedQuery, effectiveLimit, cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new ProductSuggestionResultItem(row.Sku, row.Ean, row.Name, row.Group, row.SubGroup))
            .ToArray();
    }
}
