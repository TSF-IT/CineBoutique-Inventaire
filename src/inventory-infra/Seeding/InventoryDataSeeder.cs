using System;
using System.Collections.Generic;
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
WHERE NOT EXISTS (SELECT 1 FROM ""Location"" WHERE ""Code"" = @Code AND ""ShopId"" = @ShopId);";

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
            var insertedShopCount = await EnsureShopsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var parisShopId = await GetShopIdAsync(connection, transaction, DefaultShopName, cancellationToken)
                .ConfigureAwait(false);

            var insertedLocationCount = 0;

            foreach (var seed in LocationSeeds)
            {
                var affectedRows = await connection.ExecuteAsync(
                        new CommandDefinition(
                            InsertLocationSql,
                            new
                            {
                                seed.Code,
                                seed.Label,
                                ShopId = parisShopId
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
        var seeds = new List<LocationSeed>(39);

        for (var index = 1; index <= 20; index++)
        {
            var code = $"B{index}";
            seeds.Add(new LocationSeed(code, $"Zone {code}"));
        }

        for (var index = 1; index <= 19; index++)
        {
            var code = $"S{index}";
            seeds.Add(new LocationSeed(code, $"Zone {code}"));
        }

        return seeds;
    }

    private sealed record ShopSeed(string Name);

    private sealed record LocationSeed(string Code, string Label);
}
