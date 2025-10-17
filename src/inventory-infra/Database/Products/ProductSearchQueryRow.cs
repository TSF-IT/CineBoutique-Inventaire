using System;

namespace CineBoutique.Inventory.Infrastructure.Database.Products;

internal sealed record ProductSearchQueryRow(
    Guid Id,
    string Sku,
    string Name,
    string? Ean,
    string? Code,
    string? CodeDigits,
    int MatchPriority,
    double Score);
