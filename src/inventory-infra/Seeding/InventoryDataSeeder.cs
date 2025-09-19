using System.Globalization;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Infrastructure.Seeding;

public sealed class InventoryDataSeeder
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<InventoryDataSeeder> _logger;

    public InventoryDataSeeder(IDbConnectionFactory connectionFactory, ILogger<InventoryDataSeeder> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await SeedLocationsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                await SeedProductsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError(ex, "Échec de l'initialisation des données de démonstration.");
                throw;
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task SeedLocationsAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        const string locationCountSql = "SELECT COUNT(1) FROM \"Location\";";
        var locationCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(locationCountSql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (locationCount > 0)
        {
            return;
        }

        const string insertSql = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Name\", \"CreatedAtUtc\") VALUES (@Id, @Code, @Name, @CreatedAtUtc);";
        var utcNow = DateTimeOffset.UtcNow;
        var locations = Enumerable.Range(1, 4)
            .Select(index => new
            {
                Id = Guid.NewGuid(),
                Code = index.ToString(CultureInfo.InvariantCulture),
                Name = $"Zone {index}",
                CreatedAtUtc = utcNow
            });

        foreach (var location in locations)
        {
            await connection.ExecuteAsync(new CommandDefinition(insertSql, location, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private static async Task SeedProductsAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, CancellationToken cancellationToken)
    {
        const string productCountSql = "SELECT COUNT(1) FROM \"Product\";";
        var productCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(productCountSql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (productCount > 0)
        {
            return;
        }

        const string insertSql = "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc);";
        var utcNow = DateTimeOffset.UtcNow;

        foreach (var product in GenerateProducts(50, utcNow))
        {
            await connection.ExecuteAsync(new CommandDefinition(insertSql, product, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private static IEnumerable<object> GenerateProducts(int count, DateTimeOffset createdAtUtc)
    {
        for (var index = 1; index <= count; index++)
        {
            var sku = $"SKU{index:0000}";
            var eanValue = 1_000_000_000_000L + index;
            var ean = eanValue.ToString(CultureInfo.InvariantCulture);

            yield return new
            {
                Id = Guid.NewGuid(),
                Sku = sku,
                Name = $"Produit {index:000}",
                Ean = ean,
                CreatedAtUtc = createdAtUtc
            };
        }
    }
}
