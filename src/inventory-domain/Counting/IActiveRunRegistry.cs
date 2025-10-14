namespace CineBoutique.Inventory.Domain.Counting;

public interface IActiveRunRegistry
{
    bool TryAcquire(Guid locationId, Guid runId);

    void Release(Guid locationId, Guid runId);
}
