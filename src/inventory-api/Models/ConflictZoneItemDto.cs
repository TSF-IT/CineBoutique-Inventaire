using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Models;

public sealed class ConflictZoneItemDto
{
    public Guid ProductId { get; set; }

    public string Ean { get; set; } = string.Empty;

    public IReadOnlyList<ConflictRunQuantityDto> Quantities { get; set; } = Array.Empty<ConflictRunQuantityDto>();

    public int Delta { get; set; }
}
