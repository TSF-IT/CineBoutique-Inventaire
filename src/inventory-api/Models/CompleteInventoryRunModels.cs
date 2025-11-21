namespace CineBoutique.Inventory.Api.Models
{
    public sealed record CompleteRunRequest(
        Guid? RunId,
        Guid OwnerUserId,
        short CountType,
        IReadOnlyList<CompleteRunItemRequest>? Items);

    public sealed record CompleteRunItemRequest(string? Ean, decimal Quantity, bool IsManual);

    public sealed class CompleteInventoryRunResponse
    {
        public Guid RunId { get; set; }

        public Guid InventorySessionId { get; set; }

        public Guid LocationId { get; set; }

        public short CountType { get; set; }

        public DateTimeOffset CompletedAtUtc { get; set; }

        public int ItemsCount { get; set; }

        public decimal TotalQuantity { get; set; }
    }
}
