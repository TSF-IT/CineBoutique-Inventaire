namespace CineBoutique.Inventory.Api.Services.Exceptions;

public sealed class ShopConflictException : ResourceConflictException
{
    public ShopConflictException(string message)
        : base(message)
    {
    }
}
