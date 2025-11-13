using System.Threading;
using System.Threading.Tasks;

namespace CineBoutique.Inventory.Infrastructure.Database.Inventory;

public interface ISessionRepository
{
    Task<StartRunResult> StartRunAsync(StartRunParameters parameters, CancellationToken cancellationToken);

    Task<CompleteRunResult> CompleteRunAsync(CompleteRunParameters parameters, CancellationToken cancellationToken);

    Task<ReleaseRunResult> ReleaseRunAsync(ReleaseRunParameters parameters, CancellationToken cancellationToken);

    Task<RestartRunResult> RestartRunAsync(RestartRunParameters parameters, CancellationToken cancellationToken);

    Task<SessionConflictResolutionResult> ResolveConflictsForSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task ResolveConflictsForLocationAsync(Guid locationId, CancellationToken cancellationToken);

    Task<ResetShopInventoryResult> ResetShopInventoryAsync(Guid shopId, CancellationToken cancellationToken);
}
