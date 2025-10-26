using Microsoft.AspNetCore.Authentication;

namespace CineBoutique.Inventory.Api.Infrastructure.Authentication;

public sealed class AdminHeaderAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Shared secret expected in the X-App-Token header to accept requests (optional).
    /// When null or empty, the token check is skipped.
    /// </summary>
    public string? AppToken { get; set; }
}
