using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed record StartRunRequest(Guid ShopId, Guid OwnerUserId, short CountType);

[Obsolete("Use StartRunRequest instead.")]
public sealed record StartInventoryRunRequest
{
    public Guid ShopId { get; init; }

    public Guid OwnerUserId { get; init; }

    public short CountType { get; init; }

    [Obsolete("Operator has been replaced by OwnerUserId.")]
    public string? Operator { get; init; }

    public StartRunRequest ToStartRunRequest() => new(ShopId, OwnerUserId, CountType);
}

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
