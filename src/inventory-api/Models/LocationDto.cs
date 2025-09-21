using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed record LocationDto(
    Guid Id,
    string Code,
    string Label,
    string? Description,
    bool IsBusy,
    string? InProgressBy,
    int? CountType,
    Guid? RunId,
    DateTimeOffset? StartedAtUtc);
