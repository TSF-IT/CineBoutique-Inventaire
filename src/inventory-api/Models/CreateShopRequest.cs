using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed class CreateShopRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
}
