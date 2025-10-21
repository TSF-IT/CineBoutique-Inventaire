using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ShopProductListItemDto(
    Guid Id,
    string Sku,
    string Name,
    string? Ean,
    string? Description,
    string? CodeDigits);
