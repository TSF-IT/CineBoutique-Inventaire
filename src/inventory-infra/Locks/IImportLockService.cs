namespace CineBoutique.Inventory.Infrastructure.Locks;

public interface IImportLockService
{
    Task<IAsyncDisposable?> TryAcquireForShopAsync(Guid shopId, CancellationToken cancellationToken);
}
