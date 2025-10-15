using System.Collections.ObjectModel;

namespace CineBoutique.Inventory.Domain.Counting;

public sealed class CountingSnapshot
{
    public CountingSnapshot(Guid locationId, IReadOnlyDictionary<string, int> quantities)
    {
        ArgumentNullException.ThrowIfNull(quantities);

        if (quantities.Count == 0)
        {
            throw new ArgumentException("Au moins un article est requis pour un snapshot.", nameof(quantities));
        }

        LocationId = locationId;
        Quantities = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(quantities, StringComparer.Ordinal));
    }

    public Guid LocationId { get; }

    public IReadOnlyDictionary<string, int> Quantities { get; }
}
