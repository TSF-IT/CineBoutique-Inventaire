using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Models;

public static class LocationCountStatus
{
    public const string NotStarted = "not_started";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
}

public sealed class LocationCountStatusDto
{
    public short CountType { get; set; }

    public string Status { get; set; } = LocationCountStatus.NotStarted;

    public Guid? RunId { get; set; }

    public string? OperatorDisplayName { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class LocationListItemDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public bool IsBusy { get; set; }

    public string? BusyBy { get; set; }

    public Guid? ActiveRunId { get; set; }

    public short? ActiveCountType { get; set; }

    public DateTimeOffset? ActiveStartedAtUtc { get; set; }

    public IReadOnlyList<LocationCountStatusDto> CountStatuses { get; set; } = Array.Empty<LocationCountStatusDto>();
}
