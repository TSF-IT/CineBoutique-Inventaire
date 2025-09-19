using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed class PinAuthenticationRequest
{
    [Required]
    [MinLength(4)]
    [MaxLength(12)]
    public string Pin { get; set; } = string.Empty;
}
