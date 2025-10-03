namespace CineBoutique.Inventory.Api.Services.Exceptions;

public sealed class ShopUserNotFoundException : ResourceNotFoundException
{
    public ShopUserNotFoundException()
        : base("L'utilisateur demandé est introuvable dans cette boutique.")
    {
    }
}
