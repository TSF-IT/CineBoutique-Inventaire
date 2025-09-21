using System;

namespace CineBoutique.Inventory.Api.Models;

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
}
