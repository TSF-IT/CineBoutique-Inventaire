namespace CineBoutique.Inventory.Domain.Counting;

public sealed class CountingConflictTracker
{
    private readonly Dictionary<(Guid LocationId, string Ean), TrackedItem> _trackedItems =
        new(new CountingKeyComparer());

    public ConflictEvaluation Evaluate(CountingSnapshot snapshot, int tolerance)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "La tolérance doit être positive ou nulle.");
        }

        var newlyTriggered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in snapshot.Quantities)
        {
            var key = (snapshot.LocationId, pair.Key);
            var quantity = pair.Value;

            if (!_trackedItems.TryGetValue(key, out var tracked))
            {
                _trackedItems[key] = new TrackedItem(quantity, false);
                continue;
            }

            var difference = Math.Abs(quantity - tracked.LastQuantity);
            var isInConflict = difference > tolerance;
            if (isInConflict)
            {
                newlyTriggered.Add(pair.Key);
            }

            _trackedItems[key] = tracked with { LastQuantity = quantity, IsInConflict = isInConflict };
        }

        var activeConflicts = _trackedItems
            .Where(kvp => kvp.Key.LocationId == snapshot.LocationId && kvp.Value.IsInConflict)
            .Select(kvp => kvp.Key.Ean)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ConflictEvaluation(activeConflicts, newlyTriggered.ToArray());
    }

    private sealed record TrackedItem(int LastQuantity, bool IsInConflict);

    private sealed class CountingKeyComparer : IEqualityComparer<(Guid LocationId, string Ean)>
    {
        public bool Equals((Guid LocationId, string Ean) x, (Guid LocationId, string Ean) y)
        {
            return x.LocationId == y.LocationId && string.Equals(x.Ean, y.Ean, StringComparison.Ordinal);
        }

        public int GetHashCode((Guid LocationId, string Ean) obj)
        {
            return HashCode.Combine(obj.LocationId, StringComparer.Ordinal.GetHashCode(obj.Ean));
        }
    }
}

public sealed record ConflictEvaluation(IReadOnlyCollection<string> ActiveConflicts, IReadOnlyCollection<string> NewlyTriggeredConflicts)
{
    public bool HasConflicts => ActiveConflicts.Count > 0;
}
