using System.ComponentModel.DataAnnotations;
namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductDto(
    [property: Required] Guid Id,
    [property: Required] string Sku,
    [property: Required] string Name,
    string? Ean);