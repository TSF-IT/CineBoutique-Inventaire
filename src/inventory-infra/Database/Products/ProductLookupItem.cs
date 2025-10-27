using System;

namespace CineBoutique.Inventory.Infrastructure.Database.Products;

public sealed record ProductLookupItem(
    Guid Id,
    string Sku,
    string Name,
    string? Ean,
    string? Code,
    string? CodeDigits,
    string? Group = null,
    string? SubGroup = null)
{
    public ProductLookupItem(Guid Id, string Sku, string Name, string? Ean, string? Code, string? CodeDigits)
        : this(Id, Sku, Name, Ean, Code, CodeDigits, null, null)
    {
    }
}
