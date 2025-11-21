namespace CineBoutique.Inventory.Api.Models
{
    public sealed class CompletedRunSummaryDto
    {
        public Guid RunId { get; set; }

        public Guid LocationId { get; set; }

        public string LocationCode { get; set; } = string.Empty;

        public string LocationLabel { get; set; } = string.Empty;

        public short CountType { get; set; }

        public string? OwnerDisplayName { get; set; }

        public Guid? OwnerUserId { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }

        public DateTimeOffset CompletedAtUtc { get; set; }
    }
}
