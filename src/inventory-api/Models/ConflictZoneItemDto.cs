using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed class ConflictZoneItemDto
{
    public string Ean { get; set; } = string.Empty;

    public Guid ProductId { get; set; }

    public int QtyC1 { get; set; }

    public int QtyC2 { get; set; }

    public int Delta => QtyC1 - QtyC2;
}
