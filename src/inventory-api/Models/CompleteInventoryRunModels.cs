using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Models;

public sealed class CompleteInventoryRunRequest
{
    public Guid? RunId { get; set; }

    public short CountType { get; set; }

    public string? Operator { get; set; }

    public List<CompleteInventoryRunItemRequest>? Items { get; set; }
}

public sealed class CompleteInventoryRunItemRequest
{
    public string? Ean { get; set; }

    public decimal Quantity { get; set; }

    public bool IsManual { get; set; }
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
