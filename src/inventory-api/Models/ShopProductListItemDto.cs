using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ShopProductListItemDto(
    string Sku,
    string Name,
    string? Ean,
    string? CodeDigits,
    string? Attributes,
    DateTimeOffset CreatedAtUtc);
