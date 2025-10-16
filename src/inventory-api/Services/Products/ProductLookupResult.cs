using System;
using System.Collections.Generic;
using CineBoutique.Inventory.Api.Models;

namespace CineBoutique.Inventory.Api.Services.Products;

public enum ProductLookupStatus
{
    Success,
    Conflict,
    NotFound
}

public sealed record ProductLookupMatch(string Sku, string Code);

public sealed record ProductLookupResult(
    ProductLookupStatus Status,
    ProductDto? Product,
    string OriginalCode,
    string NormalizedCode,
    string? Digits,
    IReadOnlyList<ProductLookupMatch> Matches)
{
    public static ProductLookupResult Success(string originalCode, string normalizedCode, string? digits, ProductDto product)
        => new(ProductLookupStatus.Success, product ?? throw new ArgumentNullException(nameof(product)), originalCode, normalizedCode, digits, Array.Empty<ProductLookupMatch>());

    public static ProductLookupResult Conflict(string originalCode, string normalizedCode, string digits, IReadOnlyList<ProductLookupMatch> matches)
        => new(ProductLookupStatus.Conflict, null, originalCode, normalizedCode, digits, matches ?? Array.Empty<ProductLookupMatch>());

    public static ProductLookupResult NotFound(string originalCode, string normalizedCode, string? digits)
        => new(ProductLookupStatus.NotFound, null, originalCode, normalizedCode, digits, Array.Empty<ProductLookupMatch>());
}
