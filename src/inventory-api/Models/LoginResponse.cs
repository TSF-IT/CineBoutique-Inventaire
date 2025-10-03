namespace CineBoutique.Inventory.Api.Models;

public sealed record LoginResponse(
    Guid ShopId,
    Guid UserId,
    string Login,
    string UserName,
    bool IsAdmin,
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);
