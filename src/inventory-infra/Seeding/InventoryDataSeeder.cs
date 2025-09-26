using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Infrastructure.Seeding;

public sealed class InventoryDataSeeder
{
    private static readonly ProductSeed[] DemoProducts =
    {
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "0000000000001", "DEMO-0001", "Produit démo EAN 0001"),
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "0000000000002", "DEMO-0002", "Produit démo EAN 0002"),
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"), "0000000000003", "DEMO-0003", "Produit démo EAN 0003")
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
            var operatorInfo = await GetFirstOperatorAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (operatorInfo is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Aucun utilisateur existant n'a été trouvé pour le seed démo B1..B4.");
                return;
            }

            var resolvedOperator = operatorInfo;

            _logger.LogDebug(
                "Utilisation de l'utilisateur {UserId} ({DisplayName}) pour les runs de démonstration.",
                resolvedOperator.Id,
                resolvedOperator.DisplayName);

            var productIds = await EnsureDemoProductsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            var locationCodes = new[] { "B1", "B2", "B3", "B4" };

            foreach (var code in locationCodes)
            {
                var locationId = await FindLocationIdAsync(connection, transaction, code, cancellationToken)
                    .ConfigureAwait(false);

                if (!locationId.HasValue)
                {
                    _logger.LogWarning(
                        "La zone {LocationCode} est introuvable, le seed démo est ignoré pour cette zone.",
                        code);
                    continue;
                }

                var locationSeed = BuildLocationSeed(code, locationId.Value, new ReadOnlyDictionary<string, Guid>(productIds), resolvedOperator, now);

                await EnsureInventorySessionAsync(connection, transaction, locationSeed.Session, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var run in locationSeed.Runs)
                {
                    var created = await EnsureCountingRunAsync(connection, transaction, run, cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var line in run.Lines)
                    {
                        await EnsureCountLineAsync(connection, transaction, run.Id, line, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (created)
                    {
                        _logger.LogDebug(
                            "Run {RunId} ({CountType}) seedé pour la zone {LocationCode}.",
                            run.Id,
                            run.CountType,
                            code);
                    }
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Demo seed (B1..B4) applied: B1 CT1 in-progress; B2 CT1 completed + CT2 in-progress; B3 two completed same; B4 two completed different.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Échec de l'initialisation des données de démonstration B1..B4.");
            throw;
        }
    }

    private async Task<OperatorInfo?> GetFirstOperatorAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT id AS Id, display_name AS DisplayName
FROM admin_users
ORDER BY created_at ASC
LIMIT 1;";

        var row = await connection.QuerySingleOrDefaultAsync<OperatorInfo>(
            new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return row == default ? null : row;
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
            var existing = await connection.QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(selectSql, new { definition.Ean }, transaction, cancellationToken: cancellationToken))
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

    private static async Task<Guid?> FindLocationIdAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string locationCode,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT ""Id""
FROM ""Location""
WHERE ""Code"" ILIKE @CodePattern OR ""Label"" ILIKE @LabelPattern
ORDER BY ""Code"" ASC
LIMIT 1;";

        return await connection.QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        CodePattern = locationCode,
                        LabelPattern = "%" + locationCode + "%"
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private static LocationSeed BuildLocationSeed(
        string code,
        Guid locationId,
        IReadOnlyDictionary<string, Guid> products,
        OperatorInfo operatorInfo,
        DateTimeOffset now)
    {
        return code switch
        {
            "B1" => BuildB1Seed(locationId, products, operatorInfo, now),
            "B2" => BuildB2Seed(locationId, products, operatorInfo, now),
            "B3" => BuildB3Seed(locationId, products, operatorInfo, now),
            "B4" => BuildB4Seed(locationId, products, operatorInfo, now),
            _ => throw new InvalidOperationException($"La zone {code} n'est pas prise en charge pour le seed démo.")
        };
    }

    private static LocationSeed BuildB1Seed(
        Guid locationId,
        IReadOnlyDictionary<string, Guid> products,
        OperatorInfo operatorInfo,
        DateTimeOffset now)
    {
        var runStart = now.AddMinutes(-15);

        var session = new InventorySessionSeed(
            Guid.Parse("20000000-0000-0000-0000-00000000B101"),
            "Inventaire démo B1",
            runStart,
            null);

        var run = new CountingRunSeed(
            Guid.Parse("10000000-0000-0000-0000-00000000B101"),
            session.Id,
            locationId,
            1,
            runStart,
            null,
            operatorInfo.DisplayName,
            new[]
            {
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B101"),
                    products,
                    "0000000000001",
                    5m,
                    runStart.AddMinutes(5)),
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B102"),
                    products,
                    "0000000000002",
                    2m,
                    runStart.AddMinutes(7))
            });

        return new LocationSeed(session, new[] { run });
    }

    private static LocationSeed BuildB2Seed(
        Guid locationId,
        IReadOnlyDictionary<string, Guid> products,
        OperatorInfo operatorInfo,
        DateTimeOffset now)
    {
        var ct1Start = now.AddMinutes(-50);
        var ct1End = now.AddMinutes(-40);
        var ct2Start = now.AddMinutes(-10);

        var session = new InventorySessionSeed(
            Guid.Parse("20000000-0000-0000-0000-00000000B201"),
            "Inventaire démo B2",
            ct1Start,
            null);

        var ct1Run = new CountingRunSeed(
            Guid.Parse("10000000-0000-0000-0000-00000000B201"),
            session.Id,
            locationId,
            1,
            ct1Start,
            ct1End,
            operatorInfo.DisplayName,
            new[]
            {
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B201"),
                    products,
                    "0000000000001",
                    3m,
                    ct1Start.AddMinutes(6)),
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B202"),
                    products,
                    "0000000000003",
                    1m,
                    ct1Start.AddMinutes(12))
            });

        var ct2Run = new CountingRunSeed(
            Guid.Parse("10000000-0000-0000-0000-00000000B202"),
            session.Id,
            locationId,
            2,
            ct2Start,
            null,
            operatorInfo.DisplayName,
            new[]
            {
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B221"),
                    products,
                    "0000000000001",
                    4m,
                    ct2Start.AddMinutes(4))
            });

        return new LocationSeed(session, new[] { ct1Run, ct2Run });
    }

    private static LocationSeed BuildB3Seed(
        Guid locationId,
        IReadOnlyDictionary<string, Guid> products,
        OperatorInfo operatorInfo,
        DateTimeOffset now)
    {
        var ct1Start = now.AddHours(-2);
        var ct1End = ct1Start.AddMinutes(10);
        var ct2Start = now.AddMinutes(-100);
        var ct2End = ct2Start.AddMinutes(10);

        var session = new InventorySessionSeed(
            Guid.Parse("20000000-0000-0000-0000-00000000B301"),
            "Inventaire démo B3",
            ct1Start,
            ct2End);

        var ct1Run = new CountingRunSeed(
            Guid.Parse("10000000-0000-0000-0000-00000000B301"),
            session.Id,
            locationId,
            1,
            ct1Start,
            ct1End,
            operatorInfo.DisplayName,
            new[]
            {
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B301"),
                    products,
                    "0000000000001",
                    5m,
                    ct1Start.AddMinutes(4)),
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B302"),
                    products,
                    "0000000000002",
                    2m,
                    ct1Start.AddMinutes(6)),
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B303"),
                    products,
                    "0000000000003",
                    1m,
                    ct1Start.AddMinutes(8))
            });

        var ct2Run = new CountingRunSeed(
            Guid.Parse("10000000-0000-0000-0000-00000000B302"),
            session.Id,
            locationId,
            2,
            ct2Start,
            ct2End,
            operatorInfo.DisplayName,
            new[]
            {
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B321"),
                    products,
                    "0000000000001",
                    5m,
                    ct2Start.AddMinutes(4)),
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B322"),
                    products,
                    "0000000000002",
                    2m,
                    ct2Start.AddMinutes(6)),
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B323"),
                    products,
                    "0000000000003",
                    1m,
                    ct2Start.AddMinutes(8))
            });

        return new LocationSeed(session, new[] { ct1Run, ct2Run });
    }

    private static LocationSeed BuildB4Seed(
        Guid locationId,
        IReadOnlyDictionary<string, Guid> products,
        OperatorInfo operatorInfo,
        DateTimeOffset now)
    {
        var ct1Start = now.AddMinutes(-140);
        var ct1End = ct1Start.AddMinutes(10);
        var ct2Start = now.AddMinutes(-120);
        var ct2End = ct2Start.AddMinutes(10);

        var session = new InventorySessionSeed(
            Guid.Parse("20000000-0000-0000-0000-00000000B401"),
            "Inventaire démo B4",
            ct1Start,
            ct2End);

        var ct1Run = new CountingRunSeed(
            Guid.Parse("10000000-0000-0000-0000-00000000B401"),
            session.Id,
            locationId,
            1,
            ct1Start,
            ct1End,
            operatorInfo.DisplayName,
            new[]
            {
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B401"),
                    products,
                    "0000000000001",
                    5m,
                    ct1Start.AddMinutes(4)),
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B402"),
                    products,
                    "0000000000002",
                    2m,
                    ct1Start.AddMinutes(6))
            });

        var ct2Run = new CountingRunSeed(
            Guid.Parse("10000000-0000-0000-0000-00000000B402"),
            session.Id,
            locationId,
            2,
            ct2Start,
            ct2End,
            operatorInfo.DisplayName,
            new[]
            {
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B421"),
                    products,
                    "0000000000001",
                    7m,
                    ct2Start.AddMinutes(4)),
                CreateCountLine(
                    Guid.Parse("30000000-0000-0000-0000-00000000B422"),
                    products,
                    "0000000000002",
                    2m,
                    ct2Start.AddMinutes(6))
            });

        return new LocationSeed(session, new[] { ct1Run, ct2Run });
    }

    private static CountLineSeed CreateCountLine(
        Guid lineId,
        IReadOnlyDictionary<string, Guid> products,
        string ean,
        decimal quantity,
        DateTimeOffset countedAtUtc)
    {
        if (!products.TryGetValue(ean, out var productId))
        {
            throw new InvalidOperationException($"Produit démo manquant pour l'EAN {ean}.");
        }

        return new CountLineSeed(lineId, productId, quantity, countedAtUtc);
    }

    private static async Task EnsureInventorySessionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        InventorySessionSeed session,
        CancellationToken cancellationToken)
    {
        const string selectSql = "SELECT 1 FROM \"InventorySession\" WHERE \"Id\" = @Id LIMIT 1;";
        var exists = await connection.ExecuteScalarAsync<int?>(
                new CommandDefinition(selectSql, new { session.Id }, transaction, cancellationToken: cancellationToken))
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

    private static async Task<bool> EnsureCountingRunAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CountingRunSeed run,
        CancellationToken cancellationToken)
    {
        const string selectSql = "SELECT 1 FROM \"CountingRun\" WHERE \"Id\" = @Id LIMIT 1;";
        var exists = await connection.ExecuteScalarAsync<int?>(
                new CommandDefinition(selectSql, new { run.Id }, transaction, cancellationToken: cancellationToken))
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

    private static async Task EnsureCountLineAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid runId,
        CountLineSeed line,
        CancellationToken cancellationToken)
    {
        const string selectSql = "SELECT 1 FROM \"CountLine\" WHERE \"CountingRunId\" = @RunId AND \"ProductId\" = @ProductId LIMIT 1;";
        var exists = await connection.ExecuteScalarAsync<int?>(
                new CommandDefinition(
                    selectSql,
                    new { RunId = runId, line.ProductId },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (exists.HasValue)
        {
            return;
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
                        line.ProductId,
                        line.Quantity,
                        line.CountedAtUtc
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private sealed record OperatorInfo(Guid Id, string DisplayName);

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

    private sealed record CountLineSeed(Guid Id, Guid ProductId, decimal Quantity, DateTimeOffset CountedAtUtc);

    private readonly struct LocationSeed
    {
        public LocationSeed(InventorySessionSeed session, IReadOnlyList<CountingRunSeed> runs)
        {
            Session = session;
            Runs = runs;
        }

        public InventorySessionSeed Session { get; }

        public IReadOnlyList<CountingRunSeed> Runs { get; }
    }
}
