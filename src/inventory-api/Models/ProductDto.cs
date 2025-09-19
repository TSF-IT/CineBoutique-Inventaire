namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductDto(Guid Id, string Sku, string Name, string? Ean);
