using System;

namespace CineBoutique.Inventory.Api.Models;

public sealed class ConflictRunQuantityDto
{
    public Guid RunId { get; set; }

    public short CountType { get; set; }

    public int Quantity { get; set; }
}
