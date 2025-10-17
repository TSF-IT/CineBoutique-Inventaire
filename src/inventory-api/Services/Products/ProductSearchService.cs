using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database.Products;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductSearchService : IProductSearchService
{
    private readonly IProductLookupRepository _repository;

    public ProductSearchService(IProductLookupRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<ProductSearchResultItem>> SearchAsync(string code, int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var normalizedCode = (code ?? string.Empty).Trim();
        if (normalizedCode.Length == 0)
        {
            return Array.Empty<ProductSearchResultItem>();
        }

        var effectiveLimit = Math.Clamp(limit, 1, 50);

        var candidates = await _repository
            .SearchProductsAsync(normalizedCode, effectiveLimit, cancellationToken)
            .ConfigureAwait(false);

        if (candidates is null || candidates.Count == 0)
        {
            return Array.Empty<ProductSearchResultItem>();
        }

        var results = new List<ProductSearchResultItem>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (candidate is null)
            {
                continue;
            }

            var codeToExpose = ResolveCode(candidate);
            results.Add(new ProductSearchResultItem(candidate.Sku, codeToExpose, candidate.Name));
        }

        return results;
    }

    private static string? ResolveCode(ProductLookupItem candidate)
    {
        if (candidate is null)
        {
            return null;
        }

        return Normalize(candidate.Code)
            ?? Normalize(candidate.Ean)
            ?? Normalize(candidate.CodeDigits);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
