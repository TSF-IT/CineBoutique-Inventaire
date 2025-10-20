using System;

namespace CineBoutique.Inventory.Infrastructure.Admin;

public sealed class DuplicateUserException : Exception
{
    public DuplicateUserException(string message) : base(message) { }
}
