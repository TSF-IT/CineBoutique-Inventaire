using System;
using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed class DeleteShopRequest
{
    [Required]
    public Guid Id { get; set; }
}
