using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Models;

public sealed class CompletedRunDetailDto
{
    public Guid RunId { get; set; }

    public Guid LocationId { get; set; }

    public string LocationCode { get; set; } = string.Empty;

    public string LocationLabel { get; set; } = string.Empty;

    public short CountType { get; set; }

    public string? OperatorDisplayName { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public IReadOnlyList<CompletedRunDetailItemDto> Items { get; set; }
        = Array.Empty<CompletedRunDetailItemDto>();
}

public sealed class CompletedRunDetailItemDto
{
    public Guid ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Ean { get; set; }

    public decimal Quantity { get; set; }
}
