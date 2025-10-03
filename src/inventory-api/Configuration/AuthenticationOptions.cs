namespace CineBoutique.Inventory.Api.Configuration;

public sealed class AuthenticationOptions
{
    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public int TokenLifetimeMinutes { get; set; } = 30;
}
