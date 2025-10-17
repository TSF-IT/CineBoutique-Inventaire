namespace CineBoutique.Inventory.Infrastructure.Database.Products;

public sealed record ProductSuggestionItem(
    string Sku,
    string? Ean,
    string Name,
    string? Group,
    string? SubGroup);
