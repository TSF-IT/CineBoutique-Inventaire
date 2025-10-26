using System;

namespace CineBoutique.Inventory.Infrastructure.Database.Inventory.InternalRows;

internal sealed class LocationMetadataRow
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public bool Disabled { get; set; }
}

internal sealed class ActiveCountingRunRow
{
    public Guid RunId { get; set; }

    public Guid InventorySessionId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public Guid? OwnerUserId { get; set; }

    public string? OperatorDisplayName { get; set; }
}
