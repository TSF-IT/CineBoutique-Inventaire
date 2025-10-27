namespace CineBoutique.Inventory.Api.Services.Products;

public sealed record ProductSearchResultItem(string Sku, string? Code, string Name, string? Group = null, string? SubGroup = null);

