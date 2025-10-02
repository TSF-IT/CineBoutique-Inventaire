using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed class ShopUserDto
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public string Login { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public bool Disabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class CreateShopUserRequest
{
    [Required]
    [MaxLength(128)]
    public string Login { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    [MaxLength(256)]
    public string? Secret { get; set; }
}

public sealed class UpdateShopUserRequest
{
    [Required]
    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public bool Disabled { get; set; }

    [MaxLength(256)]
    public string? Secret { get; set; }
}
