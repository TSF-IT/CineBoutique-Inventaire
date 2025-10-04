using System;
using System.Collections.Generic;
using System.Linq;

namespace CineBoutique.Inventory.Api.Models;

public sealed record CompleteRunRequest(
    Guid? RunId,
    Guid OwnerUserId,
    short CountType,
    IReadOnlyList<CompleteRunItemRequest>? Items);

[Obsolete("Use CompleteRunRequest instead.")]
public sealed record CompleteInventoryRunRequest
{
    public Guid? RunId { get; init; }

    public Guid OwnerUserId { get; init; }

    public short CountType { get; init; }

    public IReadOnlyList<CompleteInventoryRunItemRequest>? Items { get; init; }

    [Obsolete("Operator has been replaced by OwnerUserId.")]
    public string? Operator { get; init; }

    public CompleteRunRequest ToCompleteRunRequest()
    {
        IReadOnlyList<CompleteRunItemRequest>? normalizedItems = Items?.Select(item => item.ToCompleteRunItemRequest()).ToArray();
        return new CompleteRunRequest(RunId, OwnerUserId, CountType, normalizedItems);
    }
}

public sealed record CompleteRunItemRequest(string? Ean, decimal Quantity, bool IsManual);

[Obsolete("Use CompleteRunItemRequest instead.")]
public sealed record CompleteInventoryRunItemRequest
{
    public string? Ean { get; init; }

    public decimal Quantity { get; init; }

    public bool IsManual { get; init; }

    public CompleteRunItemRequest ToCompleteRunItemRequest() => new(Ean, Quantity, IsManual);
}

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
