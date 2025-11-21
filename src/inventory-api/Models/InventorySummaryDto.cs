namespace CineBoutique.Inventory.Api.Models
{
    public sealed class InventorySummaryDto
    {
        public int ActiveSessions { get; set; }

        public int OpenRuns { get; set; }

        public int CompletedRuns { get; set; }

        public int Conflicts { get; set; }

        public DateTimeOffset? LastActivityUtc { get; set; }

        public IReadOnlyList<OpenRunSummaryDto> OpenRunDetails { get; set; } = [];

        public IReadOnlyList<CompletedRunSummaryDto> CompletedRunDetails { get; set; } = [];

        public IReadOnlyList<ConflictZoneSummaryDto> ConflictZones { get; set; } = [];
    }
}
