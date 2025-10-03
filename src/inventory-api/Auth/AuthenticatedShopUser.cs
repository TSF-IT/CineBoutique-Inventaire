namespace CineBoutique.Inventory.Api.Auth;

public sealed record AuthenticatedShopUser(
    Guid UserId,
    Guid ShopId,
    string Login,
    string DisplayName,
    bool IsAdmin);
