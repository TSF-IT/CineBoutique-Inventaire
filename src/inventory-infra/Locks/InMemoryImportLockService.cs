namespace CineBoutique.Inventory.Infrastructure.Locks;

using System.Collections.Concurrent;
using System.Threading;

public sealed class InMemoryImportLockService : IImportLockService
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable?> TryAcquireForShopAsync(Guid shopId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var semaphore = _locks.GetOrAdd(shopId, static _ => new SemaphoreSlim(1, 1));
        if (await semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new LockHandle(semaphore);
        }

        return null;
    }

    private sealed class LockHandle : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public LockHandle(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
