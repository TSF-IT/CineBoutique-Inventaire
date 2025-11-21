using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed class UpdateShopUserRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Login { get; set; } = string.Empty;

    [Required]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public bool? Disabled { get; set; }
}
