using System;

namespace CineBoutique.Inventory.Api.Services.Exceptions;

public class ResourceConflictException : Exception
{
    public ResourceConflictException(string message)
        : base(message)
    {
    }
}
