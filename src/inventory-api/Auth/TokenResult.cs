namespace CineBoutique.Inventory.Api.Auth;

public sealed record TokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc);
