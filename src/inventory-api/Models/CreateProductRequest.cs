namespace CineBoutique.Inventory.Api.Models
{
    public sealed record CreateProductRequest
    {
        public required string Sku { get; init; }

        public required string Name { get; init; }

        public string? Ean { get; init; }
    }
}
