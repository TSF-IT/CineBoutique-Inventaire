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

internal sealed class ExistingRunRow
{
    public Guid Id { get; set; }

    public Guid InventorySessionId { get; set; }

    public Guid LocationId { get; set; }

    public Guid? OwnerUserId { get; set; }

    public short CountType { get; set; }

    public string? OperatorDisplayName { get; set; }
}

internal sealed class ReleaseRunRow
{
    public Guid InventorySessionId { get; set; }

    public Guid ShopId { get; set; }

    public Guid? OwnerUserId { get; set; }

    public string? OperatorDisplayName { get; set; }
}

internal sealed class ProductLookupRow
{
    public Guid Id { get; set; }

    public string Ean { get; set; } = string.Empty;

    public string? CodeDigits { get; set; }
}

internal sealed class SessionConflictObservationRow
{
    public Guid ProductId { get; set; }

    public string? Ean { get; set; }

    public string? Sku { get; set; }

    public string? Name { get; set; }

    public Guid CountLineId { get; set; }

    public Guid RunId { get; set; }

    public short CountType { get; set; }

    public Guid? OwnerUserId { get; set; }

    public string? OperatorDisplayName { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public decimal Quantity { get; set; }
}

internal sealed class ExistingConflictRow
{
    public Guid ConflictId { get; set; }

    public Guid CountLineId { get; set; }

    public Guid ProductId { get; set; }

    public bool IsResolved { get; set; }

    public int? ResolvedQuantity { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class ShopRunRow
{
    public Guid RunId { get; set; }

    public Guid SessionId { get; set; }

    public Guid LocationId { get; set; }
}
