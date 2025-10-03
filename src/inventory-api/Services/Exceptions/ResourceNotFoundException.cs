using System;

namespace CineBoutique.Inventory.Api.Services.Exceptions;

public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message)
        : base(message)
    {
    }
}
