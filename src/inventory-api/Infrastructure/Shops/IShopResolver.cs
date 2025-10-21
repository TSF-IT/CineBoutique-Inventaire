using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CineBoutique.Inventory.Api.Infrastructure.Shops;

public interface IShopResolver
{
    Guid? TryGetFromUser(HttpContext? httpContext);

    Task<Guid> GetDefaultForBackCompatAsync(IDbConnection connection, CancellationToken cancellationToken);
}
