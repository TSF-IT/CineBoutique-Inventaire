using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace CineBoutique.Inventory.Api.Infrastructure.Shops;

internal static class ShopIdResolver
{
    private const string FirstShopSql = "SELECT \"Id\" FROM \"Shop\" ORDER BY \"Name\" LIMIT 1;";

    public static async Task<Guid> ResolveAsync(IDbConnection connection, Guid requestedShopId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (requestedShopId != Guid.Empty)
        {
            return requestedShopId;
        }

        var command = new CommandDefinition(FirstShopSql, cancellationToken: cancellationToken);
        var resolved = await connection.ExecuteScalarAsync<Guid?>(command).ConfigureAwait(false);
        if (!resolved.HasValue)
        {
            throw new InvalidOperationException("Aucune boutique n'est configurée pour les opérations héritées.");
        }

        return resolved.Value;
    }
}
