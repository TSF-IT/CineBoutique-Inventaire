namespace CineBoutique.Inventory.Domain.Counting;

public sealed class CountLine
{
    public CountLine(string ean, int quantity)
    {
        if (string.IsNullOrWhiteSpace(ean))
        {
            throw new ArgumentException("EAN est obligatoire.", nameof(ean));
        }

        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "La quantité doit être positive ou nulle.");
        }

        Ean = ean.Trim();
        Quantity = quantity;
    }

    public string Ean { get; }

    public int Quantity { get; }
}
