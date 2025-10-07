using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed class ConflictRunHeaderDto
{
    public Guid RunId { get; set; }

    public short CountType { get; set; }

    public DateTime CompletedAtUtc { get; set; }

    public string? OwnerDisplayName { get; set; }
}
