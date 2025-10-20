using System;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportPayloadTooLargeException : Exception
{
    public ProductImportPayloadTooLargeException()
    {
    }

    public ProductImportPayloadTooLargeException(string message)
        : base(message)
    {
    }

    public ProductImportPayloadTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ProductImportPayloadTooLargeException(long maxBytes)
        : base(CreateMessage(maxBytes))
    {
        MaxBytes = maxBytes;
    }

    public long MaxBytes { get; }

    private static string CreateMessage(long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        return $"Le fichier d'import dépasse la limite autorisée de {maxBytes} octets.";
    }
}
