using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Models;

public sealed class ConflictZoneDetailDto
{
    public Guid LocationId { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string LocationLabel { get; set; } = string.Empty;

    public IReadOnlyList<ConflictZoneItemDto> Items { get; set; } = Array.Empty<ConflictZoneItemDto>();
}
