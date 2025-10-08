using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Models;

public sealed class ConflictZoneItemDto
{
    public string Sku { get; set; } = string.Empty;

    public string Ean { get; set; } = string.Empty;

    public Guid ProductId { get; set; }

    public int QtyC1 { get; set; }

    public int QtyC2 { get; set; }

    public IReadOnlyList<ConflictRunQtyDto> AllCounts { get; set; } = Array.Empty<ConflictRunQtyDto>();

    public int Delta => QtyC1 - QtyC2;
}
