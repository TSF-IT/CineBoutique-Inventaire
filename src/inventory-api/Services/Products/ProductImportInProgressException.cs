using System;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportInProgressException : Exception
{
    public ProductImportInProgressException()
        : base("Un import produits est déjà en cours.")
    {
    }
}
