namespace CineBoutique.Inventory.Api.Models
{
    public sealed class ConflictZoneDetailDto
    {
        public Guid LocationId { get; set; }

        public string LocationCode { get; set; } = string.Empty;

        public string LocationLabel { get; set; } = string.Empty;

        public IReadOnlyList<ConflictRunHeaderDto> Runs { get; set; } = Array.Empty<ConflictRunHeaderDto>();

        public IReadOnlyList<ConflictZoneItemDto> Items { get; set; } = Array.Empty<ConflictZoneItemDto>();
    }
}
