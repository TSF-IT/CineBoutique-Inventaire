using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed class CreateShopUserRequest
{
    [Required]
    public string Login { get; set; } = string.Empty;

    [Required]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }
}
