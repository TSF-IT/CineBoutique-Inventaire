using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database.Products;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductSearchService : IProductSearchService
{
    private static readonly Regex DigitsOnlyRegex = new("[^0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        var results = new List<ProductSearchResultItem>(Math.Min(limit, 32));
        var seenProductIds = new HashSet<Guid>();

        var skuMatch = await _repository.FindBySkuAsync(normalizedCode, cancellationToken).ConfigureAwait(false);
        if (skuMatch is not null)
        {
            TryAddResult(results, seenProductIds, skuMatch, limit);
        }

        if (results.Count < limit)
        {
            var rawMatches = await _repository.FindByRawCodeAsync(normalizedCode, cancellationToken).ConfigureAwait(false);
            AppendResults(results, seenProductIds, rawMatches, limit);
        }

        if (results.Count < limit)
        {
            var digits = ExtractDigits(normalizedCode);
            if (digits.Length > 0)
            {
                var digitMatches = await _repository.FindByCodeDigitsAsync(digits, cancellationToken).ConfigureAwait(false);
                AppendResults(results, seenProductIds, digitMatches, limit);
            }
        }

        return results;
    }

    private static void AppendResults(
        ICollection<ProductSearchResultItem> results,
        ISet<Guid> seenProductIds,
        IReadOnlyCollection<ProductLookupItem> candidates,
        int limit)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            if (candidate is null)
            {
                continue;
            }

            if (!TryAddResult(results, seenProductIds, candidate, limit))
            {
                continue;
            }

            if (results.Count >= limit)
            {
                break;
            }
        }
    }

    private static bool TryAddResult(
        ICollection<ProductSearchResultItem> results,
        ISet<Guid> seenProductIds,
        ProductLookupItem candidate,
        int limit)
    {
        if (results.Count >= limit)
        {
            return false;
        }

        if (!seenProductIds.Add(candidate.Id))
        {
            return false;
        }

        var code = ResolveCode(candidate);
        results.Add(new ProductSearchResultItem(candidate.Sku, code, candidate.Name));
        return true;
    }

    private static string ExtractDigits(string value)
        => DigitsOnlyRegex.Replace(value, string.Empty);

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
