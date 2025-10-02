namespace CineBoutique.Inventory.Api.Models;

public sealed record LoginResponse(
    Guid UserId,
    Guid ShopId,
    string ShopName,
    string DisplayName,
    bool IsAdmin,
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);
