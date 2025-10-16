using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database.Products;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductLookupService : IProductLookupService
{
    private static readonly Regex DigitsOnlyRegex = new("[^0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IProductLookupRepository _repository;

    public ProductLookupService(IProductLookupRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ProductLookupResult> ResolveAsync(string code, CancellationToken cancellationToken)
    {
        var originalCode = code ?? string.Empty;
        var normalizedCode = originalCode.Trim();

        if (normalizedCode.Length == 0)
        {
            return ProductLookupResult.NotFound(originalCode, normalizedCode, null);
        }

        var skuMatch = await _repository.FindBySkuAsync(normalizedCode, cancellationToken).ConfigureAwait(false);
        if (skuMatch is not null)
        {
            return ProductLookupResult.Success(originalCode, normalizedCode, null, ToDto(skuMatch));
        }

        var rawMatches = await _repository.FindByRawCodeAsync(normalizedCode, cancellationToken).ConfigureAwait(false);
        if (rawMatches.Count == 1)
        {
            return ProductLookupResult.Success(originalCode, normalizedCode, null, ToDto(rawMatches[0]));
        }

        var digits = ExtractDigits(normalizedCode);
        if (digits.Length > 0)
        {
            var digitMatches = await _repository.FindByCodeDigitsAsync(digits, cancellationToken).ConfigureAwait(false);
            if (digitMatches.Count == 1)
            {
                return ProductLookupResult.Success(originalCode, normalizedCode, digits, ToDto(digitMatches[0]));
            }

            if (digitMatches.Count > 1)
            {
                var matches = digitMatches
                    .Select(item => new ProductLookupMatch(item.Sku, (item.Code ?? item.Ean ?? string.Empty).Trim()))
                    .ToArray();

                return ProductLookupResult.Conflict(originalCode, normalizedCode, digits, matches);
            }
        }

        return ProductLookupResult.NotFound(originalCode, normalizedCode, digits.Length > 0 ? digits : null);
    }

    private static string ExtractDigits(string value)
        => DigitsOnlyRegex.Replace(value, string.Empty);

    private static ProductDto ToDto(ProductLookupItem item)
        => new(item.Id, item.Sku, item.Name, item.Ean);
}
