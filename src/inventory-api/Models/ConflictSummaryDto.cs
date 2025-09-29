using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed class ConflictSummaryDto
{
    public Guid ConflictId { get; set; }

    public Guid CountLineId { get; set; }

    public Guid CountingRunId { get; set; }

    public Guid LocationId { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string LocationLabel { get; set; } = string.Empty;

    public short CountType { get; set; }

    public string? OperatorDisplayName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
