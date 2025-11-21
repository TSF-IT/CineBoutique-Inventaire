using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed class DeleteShopUserRequest
{
    [Required]
    public Guid Id { get; set; }
}
