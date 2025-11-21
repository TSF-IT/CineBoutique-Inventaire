namespace CineBoutique.Inventory.Api.Models
{
    public sealed class CountingRunDto
    {
        public Guid Id { get; set; }
        public Guid ShopId { get; set; }
        public Guid InventorySessionId { get; set; }
        public Guid LocationId { get; set; }
        public short CountType { get; set; }
        public Guid? OwnerUserId { get; set; }
        public string? OperatorDisplayName { get; set; }
        public string Status { get; set; } = string.Empty;
        public int LinesCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime? ReleasedAtUtc { get; set; }

        public CountingRunDto()
        {
        }
    }
}
