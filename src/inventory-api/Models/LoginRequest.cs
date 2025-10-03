using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed class LoginRequest
{
    [Required]
    public Guid ShopId { get; set; }

    [Required]
    [MaxLength(128)]
    public string Login { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Secret { get; set; }
}
