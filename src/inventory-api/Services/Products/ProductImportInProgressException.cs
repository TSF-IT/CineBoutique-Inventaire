using System;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportInProgressException : Exception
{
    public ProductImportInProgressException()
        : base("Un import produits est déjà en cours.")
    {
    }

    public ProductImportInProgressException(string message)
        : base(message)
    {
    }

    public ProductImportInProgressException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
