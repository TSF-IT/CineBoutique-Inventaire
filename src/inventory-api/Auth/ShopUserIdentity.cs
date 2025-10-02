namespace CineBoutique.Inventory.Api.Auth;

public sealed record ShopUserIdentity(
    Guid UserId,
    Guid ShopId,
    string ShopName,
    string DisplayName,
    string Login,
    bool IsAdmin);
