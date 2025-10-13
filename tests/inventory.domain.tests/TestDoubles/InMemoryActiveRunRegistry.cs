using CineBoutique.Inventory.Domain.Counting;

namespace CineBoutique.Inventory.Domain.Tests.TestDoubles;

internal sealed class InMemoryActiveRunRegistry : IActiveRunRegistry
{
    private readonly Dictionary<Guid, Guid> _activeRunsByLocation = new();

    public bool TryAcquire(Guid locationId, Guid runId)
    {
        if (_activeRunsByLocation.TryGetValue(locationId, out var existingRunId))
        {
            return existingRunId == runId;
        }

        _activeRunsByLocation[locationId] = runId;
        return true;
    }

    public void Release(Guid locationId, Guid runId)
    {
        if (_activeRunsByLocation.TryGetValue(locationId, out var existingRunId) && existingRunId == runId)
        {
            _activeRunsByLocation.Remove(locationId);
        }
    }
}
