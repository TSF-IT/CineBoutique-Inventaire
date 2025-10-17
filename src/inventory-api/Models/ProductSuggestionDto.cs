namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductSuggestionDto(
    string Sku,
    string? Ean,
    string Name,
    string? Group,
    string? SubGroup);
