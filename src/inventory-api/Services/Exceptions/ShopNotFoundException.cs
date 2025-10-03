namespace CineBoutique.Inventory.Api.Services.Exceptions;

public sealed class ShopNotFoundException : ResourceNotFoundException
{
    public ShopNotFoundException()
        : base("La boutique demandée est introuvable.")
    {
    }
}
