namespace CineBoutique.Inventory.Api.Infrastructure.Shops;

using System.Data;

public interface IShopResolver
{
    Task<Guid> GetDefaultForBackCompatAsync(IDbConnection connection, CancellationToken cancellationToken);
}
