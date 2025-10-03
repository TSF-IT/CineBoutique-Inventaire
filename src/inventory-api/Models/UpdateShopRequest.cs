using System;
using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed class UpdateShopRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;
}
