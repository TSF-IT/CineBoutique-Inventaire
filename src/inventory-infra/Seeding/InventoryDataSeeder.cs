using System.Collections.Generic;
using System.Data;
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

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await ClearExistingDataAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            var products = await SeedProductsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var locationId = await EnsureLocationAsync(connection, transaction, "B1", "Zone B1", cancellationToken)
                .ConfigureAwait(false);

            await SeedInventoryRunsAsync(connection, transaction, locationId, products, cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Échec de l'initialisation des données de démonstration.");
            throw;
        }
    }

    private static async Task<IDictionary<string, Guid>> SeedProductsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string insertSql = @"
INSERT INTO ""Product"" (""Id"", ""Sku"", ""Name"", ""Ean"", ""CreatedAtUtc"")
VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc)
ON CONFLICT (""Ean"") DO UPDATE SET
    ""Sku"" = EXCLUDED.""Sku"",
    ""Name"" = EXCLUDED.""Name"",
    ""CreatedAtUtc"" = EXCLUDED.""CreatedAtUtc""
RETURNING ""Id"", ""Ean"";";

        var utcNow = DateTimeOffset.UtcNow;

        var productDefinitions = new[]
        {
            new ProductSeed("3057065988108", "LPV-FR-001", "Liquide pour vape aux fruits rouges"),
            new ProductSeed("9798347622207", "BK-BACKLOT-PARIS", "Livre Backlot Rues de Paris"),
            new ProductSeed("3524891908353", "DAC-SERV-001", "Dacomex, serviettes nettoyantes")
        };

        var products = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var definition in productDefinitions)
        {
            var productId = await connection.QuerySingleAsync<Guid>(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        Id = Guid.NewGuid(),
                        definition.Sku,
                        definition.Name,
                        definition.Ean,
                        CreatedAtUtc = utcNow
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            products[definition.Ean] = productId;
        }

        return products;
    }

    private static async Task<Guid> EnsureLocationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string code,
        string label,
        CancellationToken cancellationToken)
    {
        const string selectSql = "SELECT \"Id\" FROM \"Location\" WHERE \"Code\" = @Code LIMIT 1;";
        var existing = await connection.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(selectSql, new { Code = code }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (existing.HasValue)
        {
            return existing.Value;
        }

        const string insertSql = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, @Code, @Label);";
        var locationId = Guid.NewGuid();

        await connection.ExecuteAsync(
            new CommandDefinition(
                insertSql,
                new { Id = locationId, Code = code, Label = label },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return locationId;
    }

    private static async Task SeedInventoryRunsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid locationId,
        IDictionary<string, Guid> products,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var reference = DateTimeOffset.UtcNow;

        const string insertSessionSql = "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\", \"CompletedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc, @CompletedAtUtc);";
        await connection.ExecuteAsync(
            new CommandDefinition(
                insertSessionSql,
                new
                {
                    Id = sessionId,
                    Name = "Inventaire Démo B1",
                    StartedAtUtc = reference.AddDays(-2),
                    CompletedAtUtc = (DateTimeOffset?)null
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var runs = CreateCountingRuns(sessionId, locationId, reference);

        const string insertRunSql = @"
INSERT INTO ""CountingRun"" (""Id"", ""InventorySessionId"", ""LocationId"", ""StartedAtUtc"", ""CompletedAtUtc"", ""CountType"", ""OperatorDisplayName"")
VALUES (@Id, @InventorySessionId, @LocationId, @StartedAtUtc, @CompletedAtUtc, @CountType, @OperatorDisplayName);";

        const string insertLineSql = @"
INSERT INTO ""CountLine"" (""Id"", ""CountingRunId"", ""ProductId"", ""Quantity"", ""CountedAtUtc"")
VALUES (@Id, @CountingRunId, @ProductId, @Quantity, @CountedAtUtc);";

        foreach (var run in runs)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertRunSql,
                    new
                    {
                        run.Id,
                        run.InventorySessionId,
                        run.LocationId,
                        run.StartedAtUtc,
                        run.CompletedAtUtc,
                        CountType = (short)run.CountType,
                        run.OperatorDisplayName
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            foreach (var line in run.Lines)
            {
                if (!products.TryGetValue(line.ProductEan, out var productId))
                {
                    throw new InvalidOperationException($"Le produit avec l'EAN {line.ProductEan} est introuvable.");
                }

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertLineSql,
                        new
                        {
                            line.Id,
                            CountingRunId = run.Id,
                            ProductId = productId,
                            line.Quantity,
                            line.CountedAtUtc
                        },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
        }
    }

    private static IReadOnlyList<CountingRunSeed> CreateCountingRuns(Guid sessionId, Guid locationId, DateTimeOffset reference)
    {
        var aliceFirstStart = reference.AddDays(-2).AddHours(-2);
        var aliceSecondStart = reference.AddDays(-1).AddHours(-1);
        var bobStart = reference.AddHours(-12);

        return new[]
        {
            new CountingRunSeed(
                Guid.NewGuid(),
                sessionId,
                locationId,
                aliceFirstStart,
                aliceFirstStart.AddMinutes(45),
                1,
                "Alice",
                new[]
                {
                    new CountLineSeed(Guid.NewGuid(), "3057065988108", 12.0m, aliceFirstStart.AddMinutes(10)),
                    new CountLineSeed(Guid.NewGuid(), "9798347622207", 7.0m, aliceFirstStart.AddMinutes(18)),
                    new CountLineSeed(Guid.NewGuid(), "3524891908353", 18.0m, aliceFirstStart.AddMinutes(27))
                }),
            new CountingRunSeed(
                Guid.NewGuid(),
                sessionId,
                locationId,
                aliceSecondStart,
                aliceSecondStart.AddMinutes(50),
                1,
                "Alice",
                new[]
                {
                    new CountLineSeed(Guid.NewGuid(), "3057065988108", 12.0m, aliceSecondStart.AddMinutes(12)),
                    new CountLineSeed(Guid.NewGuid(), "9798347622207", 7.0m, aliceSecondStart.AddMinutes(22)),
                    new CountLineSeed(Guid.NewGuid(), "3524891908353", 18.0m, aliceSecondStart.AddMinutes(35))
                }),
            new CountingRunSeed(
                Guid.NewGuid(),
                sessionId,
                locationId,
                bobStart,
                bobStart.AddMinutes(40),
                1,
                "Bob",
                new[]
                {
                    new CountLineSeed(Guid.NewGuid(), "3057065988108", 11.0m, bobStart.AddMinutes(8)),
                    new CountLineSeed(Guid.NewGuid(), "9798347622207", 7.0m, bobStart.AddMinutes(16)),
                    new CountLineSeed(Guid.NewGuid(), "3524891908353", 20.0m, bobStart.AddMinutes(28))
                })
        };
    }

    private static async Task ClearExistingDataAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            "DELETE FROM \"Conflict\";",
            "DELETE FROM \"CountLine\";",
            "DELETE FROM \"CountingRun\";",
            "DELETE FROM \"InventorySession\";",
            "DELETE FROM \"Product\";"
        };

        foreach (var command in commands)
        {
            await connection.ExecuteAsync(
                    new CommandDefinition(command, transaction: transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }
    }

    private sealed record ProductSeed(string Ean, string Sku, string Name);

    private sealed record CountingRunSeed(
        Guid Id,
        Guid InventorySessionId,
        Guid LocationId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        short CountType,
        string OperatorDisplayName,
        IReadOnlyList<CountLineSeed> Lines);

    private sealed record CountLineSeed(Guid Id, string ProductEan, decimal Quantity, DateTimeOffset CountedAtUtc);
}
