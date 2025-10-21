using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Models;

public sealed class InventorySummaryDto
{
    public int ActiveSessions { get; set; }

    public int OpenRuns { get; set; }

    public int CompletedRuns { get; set; }

    public int Conflicts { get; set; }

    public DateTimeOffset? LastActivityUtc { get; set; }

    public IReadOnlyList<OpenRunSummaryDto> OpenRunDetails { get; set; } = Array.Empty<OpenRunSummaryDto>();

    public IReadOnlyList<CompletedRunSummaryDto> CompletedRunDetails { get; set; } = Array.Empty<CompletedRunSummaryDto>();

    public IReadOnlyList<ConflictZoneSummaryDto> ConflictZones { get; set; } = Array.Empty<ConflictZoneSummaryDto>();

    public int? ProductCount { get; set; }

    public bool? HasCatalog { get; set; }
}
