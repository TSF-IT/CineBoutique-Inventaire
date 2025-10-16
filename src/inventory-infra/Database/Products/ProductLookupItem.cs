using System;

namespace CineBoutique.Inventory.Infrastructure.Database.Products;

public sealed record ProductLookupItem(
    Guid Id,
    string Sku,
    string Name,
    string? Ean,
    string? Code,
    string? CodeDigits);
