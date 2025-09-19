namespace CineBoutique.Inventory.Api.Models;

public sealed record PinAuthenticationResponse(string UserName, string AccessToken, DateTimeOffset ExpiresAtUtc);
