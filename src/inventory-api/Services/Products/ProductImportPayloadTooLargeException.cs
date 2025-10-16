using System;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportPayloadTooLargeException : Exception
{
    public ProductImportPayloadTooLargeException(long maxBytes)
        : base($"Le fichier d'import dépasse la limite autorisée de {maxBytes} octets.")
    {
        MaxBytes = maxBytes;
    }

    public long MaxBytes { get; }
}
