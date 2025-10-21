using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CineBoutique.Inventory.Api.Infrastructure.Shops;

internal sealed class ShopResolver : IShopResolver
{
    private const string CreatedAtOrderingSql = "SELECT \"Id\" FROM \"Shop\" ORDER BY \"CreatedAt\" ASC, \"Id\" ASC LIMIT 1;";
    private const string NameOrderingSql = "SELECT \"Id\" FROM \"Shop\" ORDER BY \"Name\" ASC, \"Id\" ASC LIMIT 1;";
    private const string LegacyShopName = "Legacy";
    private const string InsertLegacySql = "INSERT INTO \"Shop\" (\"Id\", \"Name\") VALUES (@Id, @Name) ON CONFLICT DO NOTHING RETURNING \"Id\";";
    private const string SelectLegacySql = "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name) ORDER BY \"CreatedAt\" ASC, \"Id\" ASC LIMIT 1;";

    private readonly ILogger<ShopResolver> _logger;

    public ShopResolver(ILogger<ShopResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Guid? TryGetFromUser(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return null;
        }

        if (httpContext.Request.Headers.TryGetValue("X-Shop-Id", out var headerValues))
        {
            foreach (var value in headerValues)
            {
                if (Guid.TryParse(value, out var parsed))
                {
                    return parsed;
                }
            }
        }

        var claim = httpContext.User?.Claims.FirstOrDefault(claim =>
            string.Equals(claim.Type, "shop_id", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(claim.Type, "shop_admin", StringComparison.OrdinalIgnoreCase));

        if (claim is not null && Guid.TryParse(claim.Value, out var claimShopId))
        {
            return claimShopId;
        }

        return null;
    }

    public async Task<Guid> GetDefaultForBackCompatAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var shopId = await TryGetFirstShopAsync(connection, cancellationToken).ConfigureAwait(false);
        if (shopId.HasValue)
        {
            return shopId.Value;
        }

        var created = await connection.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(
                InsertLegacySql,
                new { Id = Guid.NewGuid(), Name = LegacyShopName },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (created.HasValue)
        {
            _logger.LogInformation("Boutique par défaut 'Legacy' créée pour compatibilité.");
            return created.Value;
        }

        var legacy = await TrySelectLegacyShopAsync(connection, cancellationToken).ConfigureAwait(false);
        if (legacy.HasValue)
        {
            return legacy.Value;
        }

        throw new InvalidOperationException("Impossible de déterminer la boutique par défaut pour les routes héritées.");
    }

    private async Task<Guid?> TryGetFirstShopAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            return await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(CreatedAtOrderingSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            _logger.LogDebug(ex, "Colonne CreatedAt absente sur Shop, repli sur l'ordre alphabétique.");
            return await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(NameOrderingSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private async Task<Guid?> TrySelectLegacyShopAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            return await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(SelectLegacySql, new { Name = LegacyShopName }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            return await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(
                    "SELECT \"Id\" FROM \"Shop\" WHERE LOWER(\"Name\") = LOWER(@Name) ORDER BY \"Id\" ASC LIMIT 1;",
                    new { Name = LegacyShopName },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }
}
