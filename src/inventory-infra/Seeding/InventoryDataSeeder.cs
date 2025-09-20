using System.Collections.Generic;
using System.Globalization;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

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

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

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
    }

    private static async Task SeedLocationsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var zones = new List<(string Code, string Label)>();
        for (var i = 1; i <= 20; i++)
        {
            zones.Add(($"B{i}", $"Zone B{i}"));
        }

        for (var i = 1; i <= 19; i++)
        {
            zones.Add(($"S{i}", $"Zone S{i}"));
        }

        const string sql = @"
INSERT INTO ""Location"" (""Code"", ""Label"")
VALUES (@Code, @Label)
ON CONFLICT (""Code"") DO UPDATE SET ""Label"" = EXCLUDED.""Label"";
";

        foreach (var zone in zones)
        {
            var command = new CommandDefinition(sql, new { zone.Code, zone.Label }, transaction, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command).ConfigureAwait(false);
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
