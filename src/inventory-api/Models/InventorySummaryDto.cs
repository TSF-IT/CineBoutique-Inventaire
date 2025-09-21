using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed class InventorySummaryDto
{
    public int ActiveSessions { get; set; }

    public int OpenRuns { get; set; }

    public DateTimeOffset? LastActivityUtc { get; set; }
}
