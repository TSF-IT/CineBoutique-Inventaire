using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Infrastructure.Seeding;

public sealed class InventoryDataSeeder
{
    private const string DefaultShopName = "CinéBoutique Paris";

    private const string InsertShopSql = @"INSERT INTO ""Shop"" (""Name"")
VALUES (@Name)
ON CONFLICT DO NOTHING
RETURNING ""Id"";";

    private const string SelectShopIdSql = @"SELECT ""Id"" FROM ""Shop"" WHERE LOWER(""Name"") = LOWER(@Name) LIMIT 1;";

    private const string InsertLocationSql = @"INSERT INTO ""Location"" (""Code"", ""Label"", ""ShopId"")
SELECT @Code, @Label, @ShopId
WHERE NOT EXISTS (
    SELECT 1
    FROM ""Location""
    WHERE ""ShopId"" = @ShopId
      AND UPPER(""Code"") = UPPER(@Code));";

    private static readonly IReadOnlyList<ShopSeed> ShopSeeds = BuildShopSeeds();
    private static readonly IReadOnlyList<LocationSeed> LocationSeeds = BuildLocationSeeds();

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
            var insertedShopCount = await EnsureShopsAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);

            var shopIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            foreach (var shopSeed in ShopSeeds)
            {
                var shopId = await GetShopIdAsync(connection, transaction, shopSeed.Name, cancellationToken)
                    .ConfigureAwait(false);
                shopIds[shopSeed.Name] = shopId;
            }

            var insertedLocationCount = 0;

            foreach (var seed in LocationSeeds)
            {
                if (!shopIds.TryGetValue(seed.ShopName, out var shopId))
                {
                    continue;
                }

                var code = seed.Code.ToUpperInvariant();
                var label = seed.Label ?? $"Zone {code}";

                var affectedRows = await connection.ExecuteAsync(
                        new CommandDefinition(
                            InsertLocationSql,
                            new
                            {
                                Code = code,
                                Label = label,
                                ShopId = shopId
                            },
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                if (affectedRows > 0)
                {
                    insertedLocationCount += affectedRows;
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Seed terminé. {InsertedShopCount} magasins et {InsertedLocationCount} zones créés (idempotent).",
                insertedShopCount,
                insertedLocationCount);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Échec de l'initialisation des magasins/zones d'inventaire.");
            throw;
        }
    }

    private async Task<int> EnsureShopsAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var insertedCount = 0;

        foreach (var shopSeed in ShopSeeds)
        {
            var insertedId = await connection.ExecuteScalarAsync<Guid?>(
                    new CommandDefinition(
                        InsertShopSql,
                        new
                        {
                            shopSeed.Name
                        },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (insertedId.HasValue)
            {
                insertedCount++;
            }
        }

        return insertedCount;
    }

    private static async Task<Guid> GetShopIdAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        var existingId = await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(
                    SelectShopIdSql,
                    new
                    {
                        Name = name
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        var insertedId = await connection.ExecuteScalarAsync<Guid>(
                new CommandDefinition(
                    InsertShopSql,
                    new
                    {
                        Name = name
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return insertedId;
    }

    private static IReadOnlyList<ShopSeed> BuildShopSeeds()
    {
        return new List<ShopSeed>
        {
            new(DefaultShopName),
            new("CinéBoutique Bordeaux"),
            new("CinéBoutique Montpellier"),
            new("CinéBoutique Marseille"),
            new("CinéBoutique Bruxelles")
        };
    }

    private static IReadOnlyList<LocationSeed> BuildLocationSeeds()
    {
        var seeds = new List<LocationSeed>(39 + (ShopSeeds.Count - 1) * 5);

        for (var index = 1; index <= 20; index++)
        {
            var code = $"B{index}";
            seeds.Add(new LocationSeed(DefaultShopName, code, $"Zone {code}"));
        }

        for (var index = 1; index <= 19; index++)
        {
            var code = $"S{index}";
            seeds.Add(new LocationSeed(DefaultShopName, code, $"Zone {code}"));
        }

        var demoCodes = new[] { "A", "B", "C", "D", "E" };

        foreach (var shopSeed in ShopSeeds.Where(seed => !string.Equals(seed.Name, DefaultShopName, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var code in demoCodes)
            {
                var normalizedCode = code.ToUpperInvariant();
                seeds.Add(new LocationSeed(shopSeed.Name, normalizedCode, $"Zone {normalizedCode}"));
            }
        }

        return seeds;
    }

    private sealed record ShopSeed(string Name);

    private sealed record LocationSeed(string ShopName, string Code, string Label);
}
