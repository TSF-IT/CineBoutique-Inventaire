using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductSearchItemDto(
    [property: Required] string Sku,
    string? Code,
    [property: Required] string Name);
