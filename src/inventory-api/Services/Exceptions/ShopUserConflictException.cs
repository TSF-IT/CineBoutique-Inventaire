using System;

namespace CineBoutique.Inventory.Api.Services.Exceptions;

public sealed class ShopUserConflictException : ResourceConflictException
{
    public ShopUserConflictException()
    {
    }

    public ShopUserConflictException(string message)
        : base(message)
    {
    }

    public ShopUserConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
