using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed record CountingRunDto(
    Guid Id,
    Guid ShopId,
    Guid InventorySessionId,
    Guid LocationId,
    short CountType,
    Guid? OwnerUserId,
    string? OperatorDisplayName,
    string Status,
    int LinesCount,
    decimal TotalQuantity,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ReleasedAtUtc);
