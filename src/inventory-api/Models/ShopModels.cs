using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed class ShopDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class CreateShopRequest
{
    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;
}

public sealed class UpdateShopRequest
{
    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;
}
