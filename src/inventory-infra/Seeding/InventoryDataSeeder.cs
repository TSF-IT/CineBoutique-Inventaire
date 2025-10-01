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
    private const string InsertLocationSql = """
INSERT INTO ""Location"" (""Code"", ""Label"")
SELECT @Code, @Label
WHERE NOT EXISTS (SELECT 1 FROM ""Location"" WHERE ""Code"" = @Code);
""";

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
            var insertedCount = 0;

            foreach (var seed in LocationSeeds)
            {
                var affectedRows = await connection.ExecuteAsync(
                        new CommandDefinition(
                            InsertLocationSql,
                            new
                            {
                                seed.Code,
                                seed.Label
                            },
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                if (affectedRows > 0)
                {
                    insertedCount += affectedRows;
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Seed zones terminé. {InsertedCount} nouvelles zones ont été créées (opération idempotente).",
                insertedCount);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Échec de l'initialisation des zones d'inventaire.");
            throw;
        }
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

    private sealed record LocationSeed(string Code, string Label);
}
