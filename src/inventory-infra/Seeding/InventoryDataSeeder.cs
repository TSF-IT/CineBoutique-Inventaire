using System;
using System.Collections.Generic;
using System.Data;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Infrastructure.Seeding;

public sealed class InventoryDataSeeder
{
    private static readonly Guid DemoSessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static readonly Guid DemoLocationB1Id = Guid.Parse("11111111-2222-4333-8444-b1b1b1b1b1b1");
    private static readonly Guid DemoLocationB2Id = Guid.Parse("11111111-2222-4333-8444-b2b2b2b2b2b2");

    private static readonly Guid DemoRunB1Id = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid DemoRunB2Id = Guid.Parse("22222222-3333-4444-5555-666666666666");

    // IMPORTANT: pas de "produit inconnu" dans le seed. Les EAN inconnus sont gérés au runtime.
    private static readonly ProductSeed[] DemoProducts =
    {
        new(Guid.Parse("00000000-0000-4000-8000-000000000001"), "0000000000001", "DEMO-0001", "Produit démo EAN 0001"),
        new(Guid.Parse("00000000-0000-4000-8000-000000000002"), "0000000000002", "DEMO-0002", "Produit démo EAN 0002"),
        new(Guid.Parse("00000000-0000-4000-8000-000000000003"), "0000000000003", "DEMO-0003", "Produit démo EAN 0003")
    };

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
            var productIds = await EnsureDemoProductsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            var demoSeed = BuildDemoSeed(now);

            await EnsureInventorySessionAsync(connection, transaction, demoSeed.Session, cancellationToken)
                .ConfigureAwait(false);

            foreach (var location in demoSeed.Locations)
            {
                var locationId = await EnsureLocationAsync(connection, transaction, location, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var runSeed in location.Runs)
                {
                    var run = runSeed with { LocationId = locationId };
                    var createdRun = await EnsureCountingRunAsync(connection, transaction, run, cancellationToken)
                        .ConfigureAwait(false);

                    if (createdRun)
                    {
                        _logger.LogInformation(
                            "SeedRun id={RunId} session={SessionId} location={LocationId} operator={Operator}",
                            run.Id,
                            run.InventorySessionId,
                            run.LocationId,
                            run.OperatorDisplayName);
                    }

                    // Toujours assurer les lignes, même si le run existait déjà (idempotent)
                    foreach (var line in run.Lines)
                    {
                        if (!productIds.TryGetValue(line.Ean, out var productId))
                        {
                            throw new InvalidOperationException($"Produit démo manquant pour l'EAN {line.Ean}.");
                        }

                        var createdLine = await EnsureCountLineAsync(
                                connection,
                                transaction,
                                run.Id,
                                productId,
                                line,
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (createdLine)
                        {
                            _logger.LogInformation(
                                "SeedLine runId={RunId} product={ProductId} qty={Qty}",
                                run.Id,
                                productId,
                                line.Quantity);
                        }
                    }
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Jeu de données démo initialisé : session principale + runs B1 et B2.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Échec de l'initialisation des données de démonstration.");
            throw;
        }
    }

    private async Task<IDictionary<string, Guid>> EnsureDemoProductsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var products = new Dictionary<string, Guid>(StringComparer.Ordinal);

        const string selectSql = "SELECT \"Id\" FROM \"Product\" WHERE \"Ean\" = @Ean LIMIT 1;";
        const string insertSql = @"
INSERT INTO ""Product"" (""Id"", ""Sku"", ""Name"", ""Ean"", ""CreatedAtUtc"")
VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc);";

        foreach (var definition in DemoProducts)
        {
            var existing = await connection.QuerySingleOrDefaultAsync<Guid?>
                    (new CommandDefinition(selectSql, new { definition.Ean }, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (existing.HasValue)
            {
                products[definition.Ean] = existing.Value;
                continue;
            }

            await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertSql,
                        new
                        {
                            definition.Id,
                            definition.Sku,
                            definition.Name,
                            definition.Ean,
                            CreatedAtUtc = DateTimeOffset.UtcNow
                        },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            products[definition.Ean] = definition.Id;
        }

        return products;
    }

    private static DemoSeed BuildDemoSeed(DateTimeOffset now)
    {
        var session = new InventorySessionSeed(
            DemoSessionId,
            "Session de démonstration CinéBoutique",
            now.AddHours(-1),
            null);

        var runB1Start = now.AddMinutes(-10);
        var runB1 = new CountingRunSeed(
            DemoRunB1Id,
            session.Id,
            DemoLocationB1Id,
            1,
            runB1Start,
            null,
            "Amélie",
            new[]
            {
                new CountLineSeed(
                    Guid.Parse("aaaaaaaa-1111-2222-3333-444444444441"),
                    "0000000000001",
                    3m,
                    runB1Start.AddMinutes(1)),
                // Remplacé l'EAN inconnu par un EAN connu (0002)
                new CountLineSeed(
                    Guid.Parse("aaaaaaaa-1111-2222-3333-444444444442"),
                    "0000000000002",
                    5m,
                    runB1Start.AddMinutes(2))
            });

        var runB2Start = now.AddMinutes(-45);
        var runB2 = new CountingRunSeed(
            DemoRunB2Id,
            session.Id,
            DemoLocationB2Id,
            2,
            runB2Start,
            runB2Start.AddMinutes(9),
            "Bruno",
            new[]
            {
                new CountLineSeed(
                    Guid.Parse("bbbbbbbb-2222-3333-4444-555555555551"),
                    "0000000000002",
                    7m,
                    runB2Start.AddMinutes(3)),
                new CountLineSeed(
                    Guid.Parse("bbbbbbbb-2222-3333-4444-555555555552"),
                    "0000000000003",
                    1m,
                    runB2Start.AddMinutes(6))
            });

        var locations = new[]
        {
            new LocationSeed("B1", DemoLocationB1Id, "Zone B1", new[] { runB1 }),
            new LocationSeed("B2", DemoLocationB2Id, "Zone B2", new[] { runB2 })
        };

        return new DemoSeed(session, locations);
    }

    private static async Task EnsureInventorySessionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        InventorySessionSeed session,
        CancellationToken cancellationToken)
    {
        const string selectSql = "SELECT 1 FROM \"InventorySession\" WHERE \"Id\" = @Id LIMIT 1;";
        var exists = await connection.ExecuteScalarAsync<int?>
                (new CommandDefinition(selectSql, new { session.Id }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (exists.HasValue)
        {
            return;
        }

        const string insertSql = @"
INSERT INTO ""InventorySession"" (""Id"", ""Name"", ""StartedAtUtc"", ""CompletedAtUtc"")
VALUES (@Id, @Name, @StartedAtUtc, @CompletedAtUtc);";

        await connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        session.Id,
                        session.Name,
                        session.StartedAtUtc,
                        session.CompletedAtUtc
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private static async Task<Guid> EnsureLocationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        LocationSeed location,
        CancellationToken cancellationToken)
    {
        const string selectSql = "SELECT \"Id\" FROM \"Location\" WHERE \"Code\" = @Code LIMIT 1;";
        var existing = await connection.QuerySingleOrDefaultAsync<Guid?>
                (new CommandDefinition(selectSql, new { location.Code }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (existing.HasValue)
        {
            return existing.Value;
        }

        const string insertSql =
            "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\") VALUES (@Id, @Code, @Label);";

        await connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        location.Id,
                        location.Code,
                        location.Label
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return location.Id;
    }

    private static async Task<bool> EnsureCountingRunAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CountingRunSeed run,
        CancellationToken cancellationToken)
    {
        const string selectSql = "SELECT 1 FROM \"CountingRun\" WHERE \"Id\" = @Id LIMIT 1;";
        var exists = await connection.ExecuteScalarAsync<int?>
                (new CommandDefinition(selectSql, new { run.Id }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (exists.HasValue)
        {
            return false;
        }

        const string insertSql = @"
INSERT INTO ""CountingRun"" (""Id"", ""InventorySessionId"", ""LocationId"", ""StartedAtUtc"", ""CompletedAtUtc"", ""CountType"", ""OperatorDisplayName"")
VALUES (@Id, @InventorySessionId, @LocationId, @StartedAtUtc, @CompletedAtUtc, @CountType, @OperatorDisplayName);";

        await connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        run.Id,
                        run.InventorySessionId,
                        run.LocationId,
                        run.StartedAtUtc,
                        run.CompletedAtUtc,
                        CountType = run.CountType,
                        run.OperatorDisplayName
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return true;
    }

    private static async Task<bool> EnsureCountLineAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid runId,
        Guid productId,
        CountLineSeed line,
        CancellationToken cancellationToken)
    {
        // 1) Idempotence stricte: même Id déjà présent
        const string selectByIdSql = "SELECT 1 FROM \"CountLine\" WHERE \"Id\" = @Id LIMIT 1;";
        var existsById = await connection.ExecuteScalarAsync<int?>(
                new CommandDefinition(selectByIdSql, new { line.Id }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        if (existsById.HasValue)
        {
            return false;
        }

        // 2) Idempotence métier: une ligne pour ce run et ce produit existe déjà
        const string selectByBusinessSql = "SELECT 1 FROM \"CountLine\" WHERE \"CountingRunId\" = @RunId AND \"ProductId\" = @ProductId LIMIT 1;";
        var existsByBusiness = await connection.ExecuteScalarAsync<int?>(
                new CommandDefinition(selectByBusinessSql, new { RunId = runId, ProductId = productId }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        if (existsByBusiness.HasValue)
        {
            return false;
        }

        const string insertSql = @"
INSERT INTO ""CountLine"" (""Id"", ""CountingRunId"", ""ProductId"", ""Quantity"", ""CountedAtUtc"")
VALUES (@Id, @CountingRunId, @ProductId, @Quantity, @CountedAtUtc);";

        await connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new
                    {
                        line.Id,
                        CountingRunId = runId,
                        ProductId = productId,
                        line.Quantity,
                        line.CountedAtUtc
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return true;
    }

    private sealed record ProductSeed(Guid Id, string Ean, string Sku, string Name);

    private sealed record InventorySessionSeed(Guid Id, string Name, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc);

    private sealed record CountingRunSeed(
        Guid Id,
        Guid InventorySessionId,
        Guid LocationId,
        short CountType,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        string OperatorDisplayName,
        IReadOnlyList<CountLineSeed> Lines);

    private sealed record CountLineSeed(Guid Id, string Ean, decimal Quantity, DateTimeOffset CountedAtUtc);

    private sealed record LocationSeed(string Code, Guid Id, string Label, IReadOnlyList<CountingRunSeed> Runs);

    private sealed record DemoSeed(InventorySessionSeed Session, IReadOnlyList<LocationSeed> Locations);
}
