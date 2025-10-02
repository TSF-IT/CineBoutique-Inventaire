using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed class StartInventoryRunRequest
{
    public short CountType { get; set; }

    public string? Operator { get; set; }
}

public sealed class StartInventoryRunResponse
{
    public Guid RunId { get; set; }

    public Guid InventorySessionId { get; set; }

    public Guid LocationId { get; set; }

    public short CountType { get; set; }

    public string? OperatorDisplayName { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
}
