namespace CineBoutique.Inventory.Api.Auth;

public interface ITokenService
{
    TokenResult GenerateToken(ShopUserIdentity identity);
}
