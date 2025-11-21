namespace CineBoutique.Inventory.Api.Models
{
    public sealed record StartRunRequest(Guid ShopId, Guid OwnerUserId, short CountType);

    public sealed class StartInventoryRunResponse
    {
        public Guid RunId { get; set; }

        public Guid InventorySessionId { get; set; }

        public Guid LocationId { get; set; }

        public short CountType { get; set; }

        public Guid? OwnerUserId { get; set; }

        public string? OwnerDisplayName { get; set; }

        public string? OperatorDisplayName { get; set; }

        public DateTimeOffset StartedAtUtc { get; set; }
    }

    public sealed record RestartRunRequest(Guid OwnerUserId, short CountType);

    public sealed class ResetShopInventoryResponse
    {
        public Guid ShopId { get; init; }

        public string? ShopName { get; init; }

        public int ZonesCleared { get; init; }

        public int RunsCleared { get; init; }

        public int LinesCleared { get; init; }

        public int ConflictsCleared { get; init; }

        public int SessionsClosed { get; init; }
    }
}
