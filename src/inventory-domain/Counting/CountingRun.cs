using System.Collections.ObjectModel;

namespace CineBoutique.Inventory.Domain.Counting;

public sealed class CountingRun
{
    private readonly List<CountLine> _lines = new();

    public CountingRun(Guid locationId, int tolerance = 0)
    {
        if (tolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "La tolérance doit être positive ou nulle.");
        }

        LocationId = locationId;
        Tolerance = tolerance;
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }

    public Guid LocationId { get; }

    public CountingRunStatus Status { get; private set; } = CountingRunStatus.NotStarted;

    public int Tolerance { get; }

    public IReadOnlyCollection<CountLine> Lines => new ReadOnlyCollection<CountLine>(_lines);

    public void Start(IActiveRunRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (Status != CountingRunStatus.NotStarted)
        {
            throw new InvalidOperationException("Le run est déjà démarré ou terminé.");
        }

        if (!registry.TryAcquire(LocationId, Id))
        {
            throw new InvalidOperationException("Un run actif existe déjà pour cette zone.");
        }

        Status = CountingRunStatus.InProgress;
    }

    public void AddLine(string ean, int quantity)
    {
        if (Status != CountingRunStatus.InProgress)
        {
            throw new InvalidOperationException("Le run doit être en cours pour ajouter des lignes.");
        }

        var line = new CountLine(ean, quantity);
        _lines.Add(line);
    }

    public void Complete(IActiveRunRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (Status != CountingRunStatus.InProgress)
        {
            throw new InvalidOperationException("Le run doit être en cours pour être complété.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("Impossible de compléter un run sans lignes.");
        }

        Status = CountingRunStatus.Completed;
        registry.Release(LocationId, Id);
    }

    public IReadOnlyDictionary<string, int> AggregateByEan()
    {
        if (_lines.Count == 0)
        {
            return new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal));
        }

        var aggregate = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in _lines)
        {
            if (!aggregate.TryGetValue(line.Ean, out var existing))
            {
                aggregate[line.Ean] = line.Quantity;
                continue;
            }

            aggregate[line.Ean] = checked(existing + line.Quantity);
        }

        return new ReadOnlyDictionary<string, int>(aggregate);
    }

    public CountingSnapshot CreateSnapshot()
    {
        if (Status != CountingRunStatus.Completed)
        {
            throw new InvalidOperationException("Le run doit être complété avant de générer un snapshot.");
        }

        var aggregated = AggregateByEan();
        if (aggregated.Count == 0)
        {
            throw new InvalidOperationException("Aucune donnée agrégée disponible pour ce run.");
        }

        return new CountingSnapshot(LocationId, aggregated);
    }
}
