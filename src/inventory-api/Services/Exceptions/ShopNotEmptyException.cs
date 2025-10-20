using System;

namespace CineBoutique.Inventory.Api.Services.Exceptions;

public sealed class ShopNotEmptyException : ResourceConflictException
{
    public ShopNotEmptyException()
        : base("La boutique contient encore des données et ne peut pas être supprimée.")
    {
    }

    public ShopNotEmptyException(string message)
        : base(message)
    {
    }

    public ShopNotEmptyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
