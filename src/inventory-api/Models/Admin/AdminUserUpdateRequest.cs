using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models.Admin;

public sealed class AdminUserUpdateRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;
}
