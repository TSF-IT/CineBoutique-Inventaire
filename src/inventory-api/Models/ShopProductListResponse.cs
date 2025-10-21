using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Models;

public sealed class ShopProductListResponse
{
    public IReadOnlyList<ShopProductListItemDto> Items { get; init; } = Array.Empty<ShopProductListItemDto>();

    public int Page { get; init; }

    public int PageSize { get; init; }

    public long Total { get; init; }

    public int TotalPages { get; init; }

    public string SortBy { get; init; } = "sku";

    public string SortDir { get; init; } = "asc";

    public string? Q { get; init; }
}
