namespace CineBoutique.Inventory.Api.Infrastructure.Shops
{
    using System.Data;
    using Dapper;

    public sealed class DefaultShopResolver : IShopResolver
    {
        public async Task<Guid> GetDefaultForBackCompatAsync(IDbConnection connection, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);

            const string preferredSql = """
SELECT "Id"
FROM "Shop"
WHERE lower("Kind") = 'boutique'
ORDER BY LOWER("Name"), "Id"
LIMIT 1;
""";

            var preferred = await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(preferredSql, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (preferred.HasValue)
                return preferred.Value;

            const string fallbackSql = """
SELECT "Id"
FROM "Shop"
ORDER BY LOWER("Name"), "Id"
LIMIT 1;
""";

            var fallback = await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(fallbackSql, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (fallback.HasValue)
                return fallback.Value;

            throw new InvalidOperationException("Aucune boutique disponible pour le mode rétrocompatibilité.");
        }
    }
}
