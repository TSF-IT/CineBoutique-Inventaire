using System.Threading;
using System.Threading.Tasks;

namespace CineBoutique.Inventory.Infrastructure.Database.Products;

public interface IProductGroupRepository
{
    Task<long?> EnsureGroupAsync(string? group, string? subGroup, CancellationToken cancellationToken);
}
