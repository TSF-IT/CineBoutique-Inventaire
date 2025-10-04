using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Models;

public sealed record CompleteRunRequest(
    Guid? RunId,
    Guid OwnerUserId,
    short CountType,
    IReadOnlyList<CompleteRunItemRequest>? Items);

[Obsolete("Use CompleteRunRequest instead.")]
public sealed record CompleteInventoryRunRequest(
        Guid? RunId,
        Guid OwnerUserId,
        short CountType,
        IReadOnlyList<CompleteRunItemRequest>? Items)
    : CompleteRunRequest(RunId, OwnerUserId, CountType, Items)
{
    [Obsolete("Operator has been replaced by OwnerUserId.")]
    public string? Operator
    {
        get => null;
        init { }
    }
}

public sealed record CompleteRunItemRequest(string? Ean, decimal Quantity, bool IsManual);

[Obsolete("Use CompleteRunItemRequest instead.")]
public sealed record CompleteInventoryRunItemRequest(string? Ean, decimal Quantity, bool IsManual)
    : CompleteRunItemRequest(Ean, Quantity, IsManual);

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
