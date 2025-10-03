namespace CineBoutique.Inventory.Api.Services.Exceptions;

public sealed class ShopUserConflictException : ResourceConflictException
{
    public ShopUserConflictException(string message)
        : base(message)
    {
    }
}
