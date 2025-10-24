using System;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductCatalogueLockedException : Exception
{
    public ProductCatalogueLockedException()
    {
    }

    public ProductCatalogueLockedException(string message)
        : base(message)
    {
    }

    public ProductCatalogueLockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
