using System;
using System.Threading;
using System.Threading.Tasks;

namespace CineBoutique.Inventory.Infrastructure.Locks;

public interface IImportLockService
{
    Task<IAsyncDisposable?> TryAcquireGlobalAsync(CancellationToken cancellationToken = default);

    Task<IAsyncDisposable?> TryAcquireForShopAsync(Guid shopId, CancellationToken cancellationToken = default);
}
