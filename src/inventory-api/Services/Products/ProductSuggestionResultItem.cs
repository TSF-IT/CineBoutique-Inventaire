namespace CineBoutique.Inventory.Api.Services.Products;

public sealed record ProductSuggestionResultItem(
    string Sku,
    string? Ean,
    string Name,
    string? Group,
    string? SubGroup);
