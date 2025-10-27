using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.Database.Inventory.InternalRows;
using Dapper;
using Npgsql;

namespace CineBoutique.Inventory.Infrastructure.Database.Inventory;

public sealed class SessionRepository : ISessionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SessionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<StartRunResult> StartRunAsync(StartRunParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var columnsState = await InventoryOperatorSqlHelper
            .DetectOperatorColumnsAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var operatorSql = InventoryOperatorSqlHelper.BuildOperatorSqlFragments("cr", "owner", columnsState);

        const string selectLocationSql = """
SELECT "Id", "ShopId", "Code", "Label", "Disabled"
FROM "Location"
WHERE "Id" = @LocationId
  AND "ShopId" = @ShopId
LIMIT 1;
""";

        var location = await connection
            .QuerySingleOrDefaultAsync<LocationMetadataRow>(
                new CommandDefinition(
                    selectLocationSql,
                    new { parameters.LocationId, parameters.ShopId },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (location is null)
        {
            return new StartRunResult
            {
                Status = StartRunStatus.LocationNotFound,
                LocationId = parameters.LocationId,
                ShopId = parameters.ShopId,
                OwnerUserId = parameters.OwnerUserId
            };
        }

        if (location.Disabled)
        {
            return new StartRunResult
            {
                Status = StartRunStatus.LocationDisabled,
                LocationId = parameters.LocationId,
                ShopId = location.ShopId,
                OwnerUserId = parameters.OwnerUserId
            };
        }

        if (!await ValidateUserBelongsToShopAsync(connection, parameters.OwnerUserId, location.ShopId, cancellationToken).ConfigureAwait(false))
        {
            return new StartRunResult
            {
                Status = StartRunStatus.OwnerInvalid,
                LocationId = parameters.LocationId,
                ShopId = location.ShopId,
                OwnerUserId = parameters.OwnerUserId
            };
        }

        const string selectOwnerDisplayNameSql = """
SELECT "DisplayName"
FROM "ShopUser"
WHERE "Id" = @OwnerUserId
LIMIT 1;
""";

        var ownerDisplayName = await connection
            .ExecuteScalarAsync<string?>(
                new CommandDefinition(
                    selectOwnerDisplayNameSql,
                    new { parameters.OwnerUserId },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        ownerDisplayName = Normalize(ownerDisplayName);

        var shouldPersistOperatorDisplayName = columnsState.HasOperatorDisplayName && !columnsState.OperatorDisplayNameIsNullable;
        var storedOperatorDisplayName = shouldPersistOperatorDisplayName
            ? ownerDisplayName ?? parameters.OwnerUserId.ToString("D", CultureInfo.InvariantCulture)
            : null;

        var selectActiveSql = $"""
SELECT
    cr."Id"                AS "RunId",
    cr."InventorySessionId" AS "InventorySessionId",
    cr."StartedAtUtc"       AS "StartedAtUtc",
    {(columnsState.HasOwnerUserId ? "cr.\"OwnerUserId\"" : "NULL::uuid")} AS "OwnerUserId",
    {operatorSql.Projection} AS "OperatorDisplayName"
FROM "CountingRun" cr
JOIN "Location" l ON l."Id" = cr."LocationId"
{InventoryOperatorSqlHelper.AppendJoinClause(operatorSql.JoinClause)}
WHERE cr."LocationId" = @LocationId
  AND cr."CountType"  = @CountType
  AND cr."CompletedAtUtc" IS NULL
ORDER BY cr."StartedAtUtc" DESC
LIMIT 1;
""";

        var activeRun = await connection
            .QuerySingleOrDefaultAsync<ActiveCountingRunRow?>(
                new CommandDefinition(
                    selectActiveSql,
                    new { parameters.LocationId, parameters.CountType },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (activeRun is { } existing)
        {
            if (columnsState.HasOwnerUserId && existing.OwnerUserId is Guid ownerId && ownerId != parameters.OwnerUserId)
            {
                var ownerLabel = Normalize(existing.OperatorDisplayName) ?? "un autre utilisateur";
                return new StartRunResult
                {
                    Status = StartRunStatus.ConflictOtherOwner,
                    LocationId = parameters.LocationId,
                    ShopId = location.ShopId,
                    OwnerUserId = parameters.OwnerUserId,
                    ConflictingOwnerLabel = ownerLabel
                };
            }

            if (!columnsState.HasOwnerUserId &&
                !string.IsNullOrWhiteSpace(existing.OperatorDisplayName) &&
                !string.IsNullOrWhiteSpace(ownerDisplayName) &&
                !string.Equals(existing.OperatorDisplayName.Trim(), ownerDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return new StartRunResult
                {
                    Status = StartRunStatus.ConflictOtherOwner,
                    LocationId = parameters.LocationId,
                    ShopId = location.ShopId,
                    OwnerUserId = parameters.OwnerUserId,
                    ConflictingOwnerLabel = existing.OperatorDisplayName
                };
            }

            return new StartRunResult
            {
                Status = StartRunStatus.Success,
                LocationId = parameters.LocationId,
                ShopId = location.ShopId,
                OwnerUserId = columnsState.HasOwnerUserId
                    ? existing.OwnerUserId ?? parameters.OwnerUserId
                    : parameters.OwnerUserId,
                Run = new StartRunInfo
                {
                    RunId = existing.RunId,
                    InventorySessionId = existing.InventorySessionId,
                    LocationId = parameters.LocationId,
                    CountType = parameters.CountType,
                    OwnerUserId = columnsState.HasOwnerUserId
                        ? existing.OwnerUserId ?? parameters.OwnerUserId
                        : parameters.OwnerUserId,
                    OwnerDisplayName = ownerDisplayName,
                    OperatorDisplayName = Normalize(existing.OperatorDisplayName) ?? ownerDisplayName,
                    StartedAtUtc = existing.StartedAtUtc,
                    WasExistingRun = true
                }
            };
        }

        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var ownerColumn = columnsState.HasOwnerUserId ? ", \"OwnerUserId\"" : string.Empty;
        var ownerValue = columnsState.HasOwnerUserId ? ", @OwnerUserId" : string.Empty;
        var operatorColumn = shouldPersistOperatorDisplayName ? ", \"OperatorDisplayName\"" : string.Empty;
        var operatorValue = shouldPersistOperatorDisplayName ? ", @OperatorDisplayName" : string.Empty;

        const string insertSessionSql = """
INSERT INTO "InventorySession" ("Id", "Name", "StartedAtUtc")
VALUES (@Id, @Name, @StartedAtUtc);
""";

        var insertRunSql = $"""
INSERT INTO "CountingRun" ("Id", "InventorySessionId", "LocationId", "CountType", "StartedAtUtc"{ownerColumn}{operatorColumn})
VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAtUtc{ownerValue}{operatorValue});
""";

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertSessionSql,
                    new
                    {
                        Id = sessionId,
                        Name = $"Session zone {location.Code}",
                        StartedAtUtc = now
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertRunSql,
                    new
                    {
                        Id = runId,
                        SessionId = sessionId,
                        LocationId = parameters.LocationId,
                        CountType = parameters.CountType,
                        StartedAtUtc = now,
                        OwnerUserId = parameters.OwnerUserId,
                        OperatorDisplayName = storedOperatorDisplayName
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }

        return new StartRunResult
        {
            Status = StartRunStatus.Success,
            LocationId = parameters.LocationId,
            ShopId = location.ShopId,
            OwnerUserId = parameters.OwnerUserId,
            Run = new StartRunInfo
            {
                RunId = runId,
                InventorySessionId = sessionId,
                LocationId = parameters.LocationId,
                CountType = parameters.CountType,
                OwnerUserId = parameters.OwnerUserId,
                OwnerDisplayName = ownerDisplayName,
                OperatorDisplayName = ownerDisplayName ?? storedOperatorDisplayName,
                StartedAtUtc = now.UtcDateTime,
                WasExistingRun = false
            }
        };
    }

    public async Task<CompleteRunResult> CompleteRunAsync(CompleteRunParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var columnsState = await InventoryOperatorSqlHelper
            .DetectOperatorColumnsAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var operatorSql = InventoryOperatorSqlHelper.BuildOperatorSqlFragments("cr", "owner", columnsState);

        const string selectLocationSql = """
SELECT "Id", "ShopId", "Code", "Label", "Disabled"
FROM "Location"
WHERE "Id" = @LocationId
LIMIT 1;
""";

        var location = await connection
            .QuerySingleOrDefaultAsync<LocationMetadataRow>(
                new CommandDefinition(
                    selectLocationSql,
                    new { parameters.LocationId },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (location is null)
        {
            return new CompleteRunResult
            {
                Error = new RepositoryError
                {
                    Title = "Ressource introuvable",
                    Detail = "La zone demandée est introuvable.",
                    StatusCode = 404
                }
            };
        }

        if (location.Disabled)
        {
            return new CompleteRunResult
            {
                Error = new RepositoryError
                {
                    Title = "Zone désactivée",
                    Detail = "La zone demandée est désactivée et ne peut pas être clôturée.",
                    StatusCode = 409
                }
            };
        }

        if (!await ValidateUserBelongsToShopAsync(connection, parameters.OwnerUserId, location.ShopId, cancellationToken).ConfigureAwait(false))
        {
            return new CompleteRunResult
            {
                Error = new RepositoryError
                {
                    Title = "Requête invalide",
                    Detail = "ownerUserId n'appartient pas à la boutique fournie ou est désactivé.",
                    StatusCode = 400,
                    Metadata = new Dictionary<string, object?>
                    {
                        ["ownerUserId"] = parameters.OwnerUserId,
                        ["shopId"] = location.ShopId
                    }
                }
            };
        }

        var items = parameters.Items ?? Array.Empty<SanitizedCountLineModel>();
        if (items.Count == 0)
        {
            return new CompleteRunResult
            {
                Error = new RepositoryError
                {
                    Title = "Requête invalide",
                    Detail = "Au moins une ligne de comptage doit être fournie.",
                    StatusCode = 400
                }
            };
        }

        const string selectOwnerDisplayNameSql = """
SELECT "DisplayName"
FROM "ShopUser"
WHERE "Id" = @OwnerUserId
LIMIT 1;
""";

        var ownerDisplayName = await connection
            .ExecuteScalarAsync<string?>(
                new CommandDefinition(
                    selectOwnerDisplayNameSql,
                    new { parameters.OwnerUserId },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        ownerDisplayName = Normalize(ownerDisplayName);

        var shouldPersistOperatorDisplayName = columnsState.HasOperatorDisplayName && !columnsState.OperatorDisplayNameIsNullable;
        var storedOperatorDisplayName = shouldPersistOperatorDisplayName
            ? ownerDisplayName ?? parameters.OwnerUserId.ToString("D", CultureInfo.InvariantCulture)
            : null;

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            ExistingRunRow? existingRun = null;

            if (parameters.RunId is Guid runId)
            {
                var selectRunSql = $"""
SELECT
    cr."Id"                 AS "Id",
    cr."InventorySessionId" AS "InventorySessionId",
    cr."LocationId"         AS "LocationId",
    cr."CountType"          AS "CountType",
    {operatorSql.OwnerUserIdProjection} AS "OwnerUserId",
    {operatorSql.Projection} AS "OperatorDisplayName"
FROM "CountingRun" cr
JOIN "Location" l ON l."Id" = cr."LocationId"
{InventoryOperatorSqlHelper.AppendJoinClause(operatorSql.JoinClause)}
WHERE cr."Id" = @RunId
LIMIT 1;
""";

                existingRun = await connection
                    .QuerySingleOrDefaultAsync<ExistingRunRow>(
                        new CommandDefinition(
                            selectRunSql,
                            new { RunId = runId },
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                if (existingRun is null)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return new CompleteRunResult
                    {
                        Error = new RepositoryError
                        {
                            Title = "Ressource introuvable",
                            Detail = "Le run fourni est introuvable.",
                            StatusCode = 404
                        }
                    };
                }

                if (existingRun.LocationId != location.Id)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return new CompleteRunResult
                    {
                        Error = new RepositoryError
                        {
                            Title = "Requête invalide",
                            Detail = "Le run ne correspond pas à la zone demandée.",
                            StatusCode = 400
                        }
                    };
                }

                if (existingRun.OwnerUserId is Guid ownerId && ownerId != parameters.OwnerUserId)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return new CompleteRunResult
                    {
                        Error = new RepositoryError
                        {
                            Title = "Conflit",
                            Detail = "Le run est attribué à un autre opérateur.",
                            StatusCode = 409
                        }
                    };
                }
            }

            if (parameters.CountType == 2)
            {
                if (columnsState.HasOwnerUserId)
                {
                    const string selectFirstRunOwnerSql = """
SELECT "OwnerUserId"
FROM "CountingRun"
WHERE "LocationId" = @LocationId
  AND "CountType" = 1
  AND "CompletedAtUtc" IS NOT NULL
ORDER BY "CompletedAtUtc" DESC
LIMIT 1;
""";

                    var firstRunOwner = await connection
                        .ExecuteScalarAsync<Guid?>(
                            new CommandDefinition(
                                selectFirstRunOwnerSql,
                                new { LocationId = location.Id },
                                transaction,
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false);

                    if (firstRunOwner is Guid ownerId && ownerId == parameters.OwnerUserId)
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        return new CompleteRunResult
                        {
                            Error = new RepositoryError
                            {
                                Title = "Conflit",
                                Detail = "Le deuxième comptage doit être réalisé par un opérateur différent du premier.",
                                StatusCode = 409
                            }
                        };
                    }
                }
                else if (columnsState.HasOperatorDisplayName)
                {
                    const string selectFirstRunOperatorSql = """
SELECT "OperatorDisplayName"
FROM "CountingRun"
WHERE "LocationId" = @LocationId
  AND "CountType" = 1
  AND "CompletedAtUtc" IS NOT NULL
ORDER BY "CompletedAtUtc" DESC
LIMIT 1;
""";

                    var firstRunOperator = await connection
                        .ExecuteScalarAsync<string?>(
                            new CommandDefinition(
                                selectFirstRunOperatorSql,
                                new { LocationId = location.Id },
                                transaction,
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(firstRunOperator) &&
                        !string.IsNullOrWhiteSpace(ownerDisplayName) &&
                        string.Equals(firstRunOperator.Trim(), ownerDisplayName, StringComparison.OrdinalIgnoreCase))
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        return new CompleteRunResult
                        {
                            Error = new RepositoryError
                            {
                                Title = "Conflit",
                                Detail = "Le deuxième comptage doit être réalisé par un opérateur différent du premier.",
                                StatusCode = 409
                            }
                        };
                    }
                }
            }

            var now = parameters.CompletedAtUtc;

            Guid countingRunId;
            Guid inventorySessionId;

            if (existingRun is not null)
            {
                countingRunId = existingRun.Id;
                inventorySessionId = existingRun.InventorySessionId;
            }
            else
            {
                inventorySessionId = Guid.NewGuid();
                countingRunId = Guid.NewGuid();

                const string insertSessionSql = """
INSERT INTO "InventorySession" ("Id", "Name", "StartedAtUtc")
VALUES (@Id, @Name, @StartedAtUtc);
""";

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertSessionSql,
                        new
                        {
                            Id = inventorySessionId,
                            Name = $"Session zone {location.Code}",
                            StartedAtUtc = now
                        },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                var ownerColumn = columnsState.HasOwnerUserId ? ", \"OwnerUserId\"" : string.Empty;
                var ownerValue = columnsState.HasOwnerUserId ? ", @OwnerUserId" : string.Empty;
                var operatorColumn = shouldPersistOperatorDisplayName ? ", \"OperatorDisplayName\"" : string.Empty;
                var operatorValue = shouldPersistOperatorDisplayName ? ", @OperatorDisplayName" : string.Empty;

                var insertRunSql = $"""
INSERT INTO "CountingRun" ("Id", "InventorySessionId", "LocationId", "CountType", "StartedAtUtc", "CompletedAtUtc"{ownerColumn}{operatorColumn})
VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAtUtc, @CompletedAtUtc{ownerValue}{operatorValue});
""";

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertRunSql,
                        new
                        {
                            Id = countingRunId,
                            SessionId = inventorySessionId,
                            LocationId = location.Id,
                            CountType = parameters.CountType,
                            StartedAtUtc = now,
                            CompletedAtUtc = now,
                            parameters.OwnerUserId,
                            OperatorDisplayName = storedOperatorDisplayName
                        },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            const string updateSessionSql = """
UPDATE "InventorySession" SET "CompletedAtUtc" = @CompletedAtUtc WHERE "Id" = @SessionId;
""";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    updateSessionSql,
                    new { SessionId = inventorySessionId, CompletedAtUtc = now },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            var ownerUpdateFragment = columnsState.HasOwnerUserId ? ", \"OwnerUserId\" = @OwnerUserId" : string.Empty;
            var operatorUpdateFragment = shouldPersistOperatorDisplayName ? ", \"OperatorDisplayName\" = @OperatorDisplayName" : string.Empty;

            var updateRunSql = $"""
UPDATE "CountingRun"
SET "CountType" = @CountType,
    "CompletedAtUtc" = @CompletedAtUtc{ownerUpdateFragment}{operatorUpdateFragment}
WHERE "Id" = @RunId;
""";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    updateRunSql,
                    new
                    {
                        RunId = countingRunId,
                        CountType = parameters.CountType,
                        CompletedAtUtc = now,
                        parameters.OwnerUserId,
                        OperatorDisplayName = storedOperatorDisplayName
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            var requestedEans = items
                .Select(item => item.Ean)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            const string selectProductsSql = """
SELECT "Id", "Ean", "CodeDigits"
FROM "Product"
WHERE "Ean" = ANY(@Eans::text[]);
""";

            var productRows = (await connection
                    .QueryAsync<ProductLookupRow>(
                        new CommandDefinition(
                            selectProductsSql,
                            new { Eans = requestedEans },
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false))
                .ToList();

            var existingProducts = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var row in productRows)
            {
                if (string.IsNullOrWhiteSpace(row.Ean))
                {
                    continue;
                }

                existingProducts[row.Ean] = row.Id;
            }

            const string insertProductSql = """
INSERT INTO "Product" ("Id", "ShopId", "Sku", "Name", "Ean", "CodeDigits", "CreatedAtUtc")
VALUES (@Id, @ShopId, @Sku, @Name, @Ean, @CodeDigits, @CreatedAtUtc);
""";

            const string insertLineSql = """
INSERT INTO "CountLine" ("Id", "CountingRunId", "ProductId", "Quantity", "CountedAtUtc")
VALUES (@Id, @RunId, @ProductId, @Quantity, @CountedAtUtc);
""";

            foreach (var item in items)
            {
                if (!existingProducts.TryGetValue(item.Ean, out var productId))
                {
                    productId = Guid.NewGuid();
                    var sku = BuildUnknownSku(item.Ean);
                    var name = $"Produit inconnu EAN {item.Ean}";

                    await connection.ExecuteAsync(
                        new CommandDefinition(
                            insertProductSql,
                            new
                            {
                                Id = productId,
                                ShopId = location.ShopId,
                                Sku = sku,
                                Name = name,
                                Ean = item.Ean,
                                CodeDigits = CodeDigitsSanitizer.Build(item.Ean),
                                CreatedAtUtc = now
                            },
                            transaction,
                            cancellationToken: cancellationToken)).ConfigureAwait(false);

                    existingProducts[item.Ean] = productId;
                }

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertLineSql,
                        new
                        {
                            Id = Guid.NewGuid(),
                            RunId = countingRunId,
                            ProductId = productId,
                            Quantity = item.Quantity,
                            CountedAtUtc = now
                        },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            await ManageConflictsAsync(connection, transaction, location.Id, countingRunId, parameters.CountType, now, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new CompleteRunResult
            {
                Run = new CompleteRunInfo
                {
                    RunId = countingRunId,
                    InventorySessionId = inventorySessionId,
                    LocationId = location.Id,
                    ShopId = location.ShopId,
                    LocationCode = location.Code,
                    LocationLabel = location.Label,
                    CountType = parameters.CountType,
                    CompletedAtUtc = now,
                    ItemsCount = items.Count,
                    TotalQuantity = SumQuantities(items),
                    OwnerDisplayName = ownerDisplayName
                }
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<ReleaseRunResult> ReleaseRunAsync(ReleaseRunParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var columnsState = await InventoryOperatorSqlHelper
            .DetectOperatorColumnsAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var operatorSql = InventoryOperatorSqlHelper.BuildOperatorSqlFragments("cr", "owner", columnsState);

        var selectRunSql = $"""
SELECT
    cr."InventorySessionId" AS "InventorySessionId",
    l."ShopId"              AS "ShopId",
    {operatorSql.OwnerUserIdProjection} AS "OwnerUserId",
    {operatorSql.Projection} AS "OperatorDisplayName"
FROM "CountingRun" cr
JOIN "Location" l ON l."Id" = cr."LocationId"
{InventoryOperatorSqlHelper.AppendJoinClause(operatorSql.JoinClause)}
WHERE cr."Id" = @RunId
  AND cr."LocationId" = @LocationId
  AND cr."CompletedAtUtc" IS NULL
LIMIT 1;
""";

        var run = await connection
            .QuerySingleOrDefaultAsync<ReleaseRunRow>(
                new CommandDefinition(
                    selectRunSql,
                    new { parameters.RunId, parameters.LocationId },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (run is null)
        {
            return new ReleaseRunResult
            {
                Error = new RepositoryError
                {
                    Title = "Ressource introuvable",
                    Detail = "Aucun comptage actif pour les critères fournis.",
                    StatusCode = 404
                }
            };
        }

        if (!await ValidateUserBelongsToShopAsync(connection, parameters.OwnerUserId, run.ShopId, cancellationToken).ConfigureAwait(false))
        {
            return new ReleaseRunResult
            {
                Error = new RepositoryError
                {
                    Title = "Requête invalide",
                    Detail = "ownerUserId n'appartient pas à la boutique fournie ou est désactivé.",
                    StatusCode = 400,
                    Metadata = new Dictionary<string, object?>
                    {
                        ["ownerUserId"] = parameters.OwnerUserId,
                        ["shopId"] = run.ShopId
                    }
                }
            };
        }

        if (columnsState.HasOwnerUserId && run.OwnerUserId is Guid ownerId && ownerId != parameters.OwnerUserId)
        {
            return new ReleaseRunResult
            {
                Error = new RepositoryError
                {
                    Title = "Conflit",
                    Detail = $"Comptage détenu par {FormatOwnerConflictLabel(run.OperatorDisplayName)}.",
                    StatusCode = 409
                }
            };
        }

        if (!columnsState.HasOwnerUserId && columnsState.HasOperatorDisplayName)
        {
            const string selectOwnerDisplayNameSql = """
SELECT "DisplayName"
FROM "ShopUser"
WHERE "Id" = @OwnerUserId
LIMIT 1;
""";

            var requestedDisplayName = await connection
                .ExecuteScalarAsync<string?>(
                    new CommandDefinition(
                        selectOwnerDisplayNameSql,
                        new { parameters.OwnerUserId },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var existingOperator = Normalize(run.OperatorDisplayName);
            requestedDisplayName = Normalize(requestedDisplayName);

            if (!string.IsNullOrWhiteSpace(existingOperator) &&
                !string.IsNullOrWhiteSpace(requestedDisplayName) &&
                !string.Equals(existingOperator, requestedDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return new ReleaseRunResult
                {
                    Error = new RepositoryError
                    {
                        Title = "Conflit",
                        Detail = $"Comptage détenu par {existingOperator}.",
                        StatusCode = 409
                    }
                };
            }
        }

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            const string countLinesSql = """
SELECT COUNT(*)::int
FROM "CountLine"
WHERE "CountingRunId" = @RunId;
""";

            var lineCount = await connection
                .ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        countLinesSql,
                        new { parameters.RunId },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (lineCount > 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new ReleaseRunResult
                {
                    Error = new RepositoryError
                    {
                        Title = "Conflit",
                        Detail = "Impossible de libérer un comptage contenant des lignes enregistrées.",
                        StatusCode = 409
                    }
                };
            }

            const string deleteRunSql = """
DELETE FROM "CountingRun"
WHERE "Id" = @RunId;
""";

            await connection.ExecuteAsync(
                new CommandDefinition(
                    deleteRunSql,
                    new { parameters.RunId },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            const string countSessionRunsSql = """
SELECT COUNT(*)::int
FROM "CountingRun"
WHERE "InventorySessionId" = @SessionId;
""";

            var remainingRuns = await connection
                .ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        countSessionRunsSql,
                        new { SessionId = run.InventorySessionId },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (remainingRuns == 0)
            {
                const string deleteSessionSql = """
DELETE FROM "InventorySession"
WHERE "Id" = @SessionId;
""";

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        deleteSessionSql,
                        new { SessionId = run.InventorySessionId },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new ReleaseRunResult();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<RestartRunResult> RestartRunAsync(RestartRunParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        const string selectLocationSql = """
SELECT "Id", "ShopId", "Code", "Label", "Disabled"
FROM "Location"
WHERE "Id" = @LocationId
LIMIT 1;
""";

        var location = await connection
            .QuerySingleOrDefaultAsync<LocationMetadataRow>(
                new CommandDefinition(
                    selectLocationSql,
                    new { parameters.LocationId },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (location is null)
        {
            return new RestartRunResult
            {
                Error = new RepositoryError
                {
                    Title = "Ressource introuvable",
                    Detail = "La zone demandée est introuvable.",
                    StatusCode = 404
                }
            };
        }

        if (location.Disabled)
        {
            return new RestartRunResult
            {
                Error = new RepositoryError
                {
                    Title = "Zone désactivée",
                    Detail = "La zone demandée est désactivée et ne peut pas être relancée.",
                    StatusCode = 409
                }
            };
        }

        if (!await ValidateUserBelongsToShopAsync(connection, parameters.OwnerUserId, location.ShopId, cancellationToken).ConfigureAwait(false))
        {
            return new RestartRunResult
            {
                Error = new RepositoryError
                {
                    Title = "Requête invalide",
                    Detail = "ownerUserId n'appartient pas à la boutique fournie ou est désactivé.",
                    StatusCode = 400,
                    Metadata = new Dictionary<string, object?>
                    {
                        ["ownerUserId"] = parameters.OwnerUserId,
                        ["shopId"] = location.ShopId
                    }
                }
            };
        }

        const string updateSql = """
UPDATE "CountingRun"
SET "CompletedAtUtc" = @NowUtc
WHERE "LocationId" = @LocationId
  AND "CompletedAtUtc" IS NULL
  AND "CountType" = @CountType;
""";

        var affected = await connection
            .ExecuteAsync(
                new CommandDefinition(
                    updateSql,
                    new
                    {
                        LocationId = parameters.LocationId,
                        CountType = parameters.CountType,
                        NowUtc = parameters.RestartedAtUtc
                    },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return new RestartRunResult
        {
            Run = new RestartRunInfo
            {
                LocationId = location.Id,
                ShopId = location.ShopId,
                LocationCode = location.Code,
                LocationLabel = location.Label,
                CountType = parameters.CountType,
                RestartedAtUtc = parameters.RestartedAtUtc,
                ClosedRuns = affected
            }
        };
    }

    private static decimal SumQuantities(IReadOnlyList<SanitizedCountLineModel> items)
    {
        decimal total = 0m;

        for (var index = 0; index < items.Count; index++)
        {
            total += items[index].Quantity;
        }

        return total;
    }

    private static string FormatOwnerConflictLabel(string? label) =>
        string.IsNullOrWhiteSpace(label) ? "un autre utilisateur" : label.Trim();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildUnknownSku(string ean)
    {
        if (string.IsNullOrWhiteSpace(ean))
        {
            return $"UNK-{Guid.NewGuid():N}"[..32];
        }

        var normalized = Normalize(ean) ?? string.Empty;
        if (normalized.Length == 0)
        {
            return $"UNK-{Guid.NewGuid():N}"[..32];
        }

        var suffixMaxLength = 32 - "UNK-".Length;
        if (suffixMaxLength <= 0)
        {
            return $"UNK-{Guid.NewGuid():N}"[..32];
        }

        if (normalized.Length > suffixMaxLength)
        {
            normalized = normalized[^suffixMaxLength..];
        }

        var sku = $"UNK-{normalized}";
        return sku.Length <= 32 ? sku : sku[^32..];
    }

    private static async Task ManageConflictsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid locationId,
        Guid currentRunId,
        short countType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (countType is 1 or 2)
        {
            await ManageInitialConflictsAsync(connection, transaction, locationId, currentRunId, countType, now, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await ManageAdditionalConflictsAsync(connection, transaction, locationId, currentRunId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ManageInitialConflictsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid locationId,
        Guid currentRunId,
        short countType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var counterpartType = countType switch
        {
            1 => (short)2,
            2 => (short)1,
            _ => (short)0
        };

        if (counterpartType == 0)
        {
            return;
        }

        const string selectCounterpartRunSql = """
SELECT "Id"
FROM "CountingRun"
WHERE "LocationId" = @LocationId AND "CountType" = @Counterpart AND "CompletedAtUtc" IS NOT NULL
ORDER BY "CompletedAtUtc" DESC
LIMIT 1;
""";

        var counterpartRunId = await connection
            .QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(
                    selectCounterpartRunSql,
                    new { LocationId = locationId, Counterpart = counterpartType },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (counterpartRunId is null)
        {
            return;
        }

        const string deleteExistingConflictsSql = """
DELETE FROM "Conflict"
USING "CountLine" cl
WHERE "Conflict"."CountLineId" = cl."Id"
  AND cl."CountingRunId" IN (@CurrentRunId, @CounterpartRunId)
  AND "Conflict"."ResolvedAtUtc" IS NULL;
""";

        await connection.ExecuteAsync(
                new CommandDefinition(
                    deleteExistingConflictsSql,
                    new { CurrentRunId = currentRunId, CounterpartRunId = counterpartRunId.Value },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var runIds = new[] { currentRunId, counterpartRunId.Value };

        const string aggregateSql = """
SELECT
    cl."CountingRunId",
    p."Ean",
    p."Id" AS "ProductId",
    SUM(cl."Quantity") AS "Quantity"
FROM "CountLine" cl
JOIN "Product" p ON p."Id" = cl."ProductId"
WHERE cl."CountingRunId" = ANY(@RunIds::uuid[])
GROUP BY cl."CountingRunId", p."Id", p."Ean";
""";

        var aggregatedRows = await connection
            .QueryAsync<AggregatedCountRow>(
                new CommandDefinition(
                    aggregateSql,
                    new { RunIds = runIds },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var quantityByRun = BuildQuantityLookup(aggregatedRows);

        const string lineLookupSql = """
SELECT DISTINCT ON (cl."CountingRunId", p."Id")
    cl."CountingRunId",
    cl."Id" AS "CountLineId",
    p."Id" AS "ProductId",
    p."Ean"
FROM "CountLine" cl
JOIN "Product" p ON p."Id" = cl."ProductId"
WHERE cl."CountingRunId" = ANY(@RunIds::uuid[])
ORDER BY cl."CountingRunId", p."Id", cl."CountedAtUtc" DESC, cl."Id" DESC;
""";

        var lineReferences = await connection
            .QueryAsync<CountLineReference>(
                new CommandDefinition(
                    lineLookupSql,
                    new { RunIds = runIds },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var lineIdLookup = lineReferences
            .GroupBy(reference => reference.CountingRunId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(
                    reference => BuildProductKey(reference.ProductId, reference.Ean),
                    reference => reference.CountLineId,
                    StringComparer.Ordinal),
                EqualityComparer<Guid>.Default);

        var currentQuantities = quantityByRun.GetValueOrDefault(currentRunId, new Dictionary<string, decimal>(StringComparer.Ordinal));
        var counterpartQuantities = quantityByRun.GetValueOrDefault(counterpartRunId.Value, new Dictionary<string, decimal>(StringComparer.Ordinal));

        var allKeys = currentQuantities.Keys
            .Union(counterpartQuantities.Keys, StringComparer.Ordinal)
            .ToArray();

        var conflictInserts = new List<object>();

        foreach (var key in allKeys)
        {
            var currentQty = currentQuantities.TryGetValue(key, out var value) ? value : 0m;
            var counterpartQty = counterpartQuantities.TryGetValue(key, out var other) ? other : 0m;

            if (currentQty == counterpartQty)
            {
                continue;
            }

            var lineId = ResolveCountLineId(lineIdLookup, currentRunId, counterpartRunId.Value, key);

            if (lineId is null)
            {
                continue;
            }

            conflictInserts.Add(new
            {
                Id = Guid.NewGuid(),
                CountLineId = lineId.Value,
                Status = "open",
                Notes = (string?)null,
                CreatedAtUtc = now
            });
        }

        if (conflictInserts.Count == 0)
        {
            return;
        }

        const string insertConflictSql = """
INSERT INTO "Conflict" ("Id", "CountLineId", "Status", "Notes", "CreatedAtUtc")
VALUES (@Id, @CountLineId, @Status, @Notes, @CreatedAtUtc);
""";

        foreach (var payload in conflictInserts)
        {
            await connection.ExecuteAsync(
                    new CommandDefinition(insertConflictSql, payload, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }
    }

    private static async Task ManageAdditionalConflictsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid locationId,
        Guid currentRunId,
        CancellationToken cancellationToken)
    {
        const string conflictProductsSql = """
SELECT DISTINCT
    p."Id"  AS "ProductId",
    p."Ean" AS "Ean"
FROM "Conflict" c
JOIN "CountLine" cl ON cl."Id" = c."CountLineId"
JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
JOIN "Product" p ON p."Id" = cl."ProductId"
WHERE c."ResolvedAtUtc" IS NULL
  AND cr."LocationId" = @LocationId;
""";

        var conflictProducts = (await connection
                .QueryAsync<ConflictProductReference>(
                    new CommandDefinition(
                        conflictProductsSql,
                        new { LocationId = locationId },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false))
            .ToArray();

        if (conflictProducts.Length == 0)
        {
            return;
        }

        var conflictKeys = conflictProducts
            .Select(product => BuildProductKey(product.ProductId, product.Ean))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (conflictKeys.Length == 0)
        {
            return;
        }

        const string completedRunsSql = """
SELECT "Id"
FROM "CountingRun"
WHERE "LocationId" = @LocationId
  AND "CompletedAtUtc" IS NOT NULL;
""";

        var completedRunIds = (await connection
                .QueryAsync<Guid>(
                    new CommandDefinition(
                        completedRunsSql,
                        new { LocationId = locationId },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false))
            .ToArray();

        if (completedRunIds.Length <= 1 || !completedRunIds.Contains(currentRunId))
        {
            return;
        }

        const string aggregateSql = """
SELECT
    cl."CountingRunId",
    p."Ean",
    p."Id" AS "ProductId",
    SUM(cl."Quantity") AS "Quantity"
FROM "CountLine" cl
JOIN "Product" p ON p."Id" = cl."ProductId"
WHERE cl."CountingRunId" = ANY(@RunIds::uuid[])
GROUP BY cl."CountingRunId", p."Id", p."Ean";
""";

        var aggregatedRows = await connection
            .QueryAsync<AggregatedCountRow>(
                new CommandDefinition(
                    aggregateSql,
                    new { RunIds = completedRunIds },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var quantitiesByRun = BuildQuantityLookup(aggregatedRows);

        var currentQuantities = quantitiesByRun.GetValueOrDefault(currentRunId, new Dictionary<string, decimal>(StringComparer.Ordinal));
        var previousRunIds = completedRunIds.Where(id => id != currentRunId).ToArray();

        var hasMatch = previousRunIds.Any(previousRunId =>
        {
            var otherQuantities = quantitiesByRun.GetValueOrDefault(previousRunId, new Dictionary<string, decimal>(StringComparer.Ordinal));
            return HaveIdenticalQuantities(currentQuantities, otherQuantities, conflictKeys);
        });

        if (!hasMatch)
        {
            return;
        }

        const string resolveConflictsSql = """
DELETE FROM "Conflict"
USING "CountLine" cl
JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
WHERE "Conflict"."CountLineId" = cl."Id"
  AND cr."LocationId" = @LocationId
  AND "Conflict"."ResolvedAtUtc" IS NULL;
""";

        await connection.ExecuteAsync(
                new CommandDefinition(
                    resolveConflictsSql,
                    new { LocationId = locationId },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private static Dictionary<Guid, Dictionary<string, decimal>> BuildQuantityLookup(IEnumerable<AggregatedCountRow> aggregatedRows) =>
        aggregatedRows
            .GroupBy(row => row.CountingRunId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(
                    row => BuildProductKey(row.ProductId, row.Ean),
                    row => row.Quantity,
                    StringComparer.Ordinal),
                EqualityComparer<Guid>.Default);

    private static bool HaveIdenticalQuantities(
        IReadOnlyDictionary<string, decimal> current,
        IReadOnlyDictionary<string, decimal> reference,
        IReadOnlyCollection<string>? relevantKeys = null)
    {
        IEnumerable<string> keys = relevantKeys is { Count: > 0 }
            ? relevantKeys
            : current.Keys.Union(reference.Keys, StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var currentValue = current.TryGetValue(key, out var value) ? value : 0m;
            var referenceValue = reference.TryGetValue(key, out var other) ? other : 0m;

            if (currentValue != referenceValue)
            {
                return false;
            }
        }

        return true;
    }

    private static Guid? ResolveCountLineId(
        Dictionary<Guid, Dictionary<string, Guid>> lookup,
        Guid currentRunId,
        Guid counterpartRunId,
        string key)
    {
        if (lookup.TryGetValue(currentRunId, out var currentLines) && currentLines.TryGetValue(key, out var currentLineId))
        {
            return currentLineId;
        }

        if (lookup.TryGetValue(counterpartRunId, out var counterpartLines) && counterpartLines.TryGetValue(key, out var counterpartLineId))
        {
            return counterpartLineId;
        }

        return null;
    }

    private static string BuildProductKey(Guid productId, string? ean)
    {
        if (!string.IsNullOrWhiteSpace(ean))
        {
            return ean.Trim();
        }

        return productId.ToString("D");
    }

    private sealed record ConflictProductReference(Guid ProductId, string? Ean);

    private static async Task<bool> ValidateUserBelongsToShopAsync(
        NpgsqlConnection connection,
        Guid ownerUserId,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT 1
FROM "ShopUser"
WHERE "Id" = @ownerUserId
  AND "ShopId" = @shopId
  AND NOT "Disabled"
""";

        var command = new CommandDefinition(sql, new { ownerUserId, shopId }, cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<int?>(command).ConfigureAwait(false) is 1;
    }
}
