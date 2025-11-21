namespace CineBoutique.Inventory.Api.Models
{
    public sealed class ConflictZoneSummaryDto
    {
        public Guid LocationId { get; set; }

        public string LocationCode { get; set; } = string.Empty;

        public string LocationLabel { get; set; } = string.Empty;

        public int ConflictLines { get; set; }
    }
}
