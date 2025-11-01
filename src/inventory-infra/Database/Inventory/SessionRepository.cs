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

            await ResolveConflictsForLocationInternalAsync(connection, transaction, location.Id, cancellationToken).ConfigureAwait(false);

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

    public async Task<ResetShopInventoryResult> ResetShopInventoryAsync(Guid shopId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            const string shopNameSql = """
SELECT "Name"
FROM "Shop"
WHERE "Id" = @ShopId
LIMIT 1;
""";

            var shopName = await connection.ExecuteScalarAsync<string?>(
                    new CommandDefinition(
                        shopNameSql,
                        new { ShopId = shopId },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            const string runsSql = """
SELECT
    cr."Id"                AS "RunId",
    cr."InventorySessionId" AS "SessionId",
    cr."LocationId"        AS "LocationId"
FROM "CountingRun" cr
JOIN "Location" l ON l."Id" = cr."LocationId"
WHERE l."ShopId" = @ShopId;
""";

            var runRows = (await connection.QueryAsync<ShopRunRow>(
                    new CommandDefinition(
                        runsSql,
                        new { ShopId = shopId },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false))
                .ToArray();

            if (runRows.Length == 0)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new ResetShopInventoryResult
                {
                    ShopId = shopId,
                    ShopName = shopName,
                    RunsRemoved = 0,
                    CountLinesRemoved = 0,
                    ConflictsRemoved = 0,
                    SessionsRemoved = 0,
                    LocationsAffected = 0
                };
            }

            var runIds = runRows.Select(row => row.RunId).Distinct().ToArray();
            var sessionIds = runRows.Select(row => row.SessionId).Distinct().ToArray();
            var locationsAffected = runRows.Select(row => row.LocationId).Distinct().Count();

            const string deleteConflictsSql = """
DELETE FROM "Conflict"
USING "CountLine" cl
WHERE "Conflict"."CountLineId" = cl."Id"
  AND cl."CountingRunId" = ANY(@RunIds);
""";
            var conflictsRemoved = await connection.ExecuteAsync(
                    new CommandDefinition(
                        deleteConflictsSql,
                        new { RunIds = runIds },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            const string deleteCountLinesSql = """
DELETE FROM "CountLine"
WHERE "CountingRunId" = ANY(@RunIds);
""";
            var countLinesRemoved = await connection.ExecuteAsync(
                    new CommandDefinition(
                        deleteCountLinesSql,
                        new { RunIds = runIds },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            const string deleteRunsSql = """
DELETE FROM "CountingRun"
WHERE "Id" = ANY(@RunIds);
""";
            var runsRemoved = await connection.ExecuteAsync(
                    new CommandDefinition(
                        deleteRunsSql,
                        new { RunIds = runIds },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var sessionsRemoved = 0;
            if (sessionIds.Length > 0)
            {
                const string deleteSessionsSql = """
DELETE FROM "InventorySession"
WHERE "Id" = ANY(@SessionIds);
""";
                sessionsRemoved = await connection.ExecuteAsync(
                        new CommandDefinition(
                            deleteSessionsSql,
                            new { SessionIds = sessionIds },
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new ResetShopInventoryResult
            {
                ShopId = shopId,
                ShopName = shopName,
                RunsRemoved = runsRemoved,
                CountLinesRemoved = countLinesRemoved,
                ConflictsRemoved = conflictsRemoved,
                SessionsRemoved = sessionsRemoved,
                LocationsAffected = locationsAffected
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

    private async Task ResolveConflictsForLocationInternalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        var columnsState = await InventoryOperatorSqlHelper
            .DetectOperatorColumnsAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var operatorSql = InventoryOperatorSqlHelper.BuildOperatorSqlFragments("cr", "owner", columnsState);

        var observationSql = $"""
SELECT
    p."Id"              AS "ProductId",
    p."Ean"             AS "Ean",
    p."Sku"             AS "Sku",
    p."Name"            AS "Name",
    cl."Id"             AS "CountLineId",
    cl."CountingRunId"  AS "RunId",
    cr."InventorySessionId" AS "InventorySessionId",
    cr."CountType"      AS "CountType",
    {operatorSql.OwnerUserIdProjection} AS "OwnerUserId",
    {operatorSql.Projection}            AS "OperatorDisplayName",
    cr."CompletedAtUtc" AS "CompletedAtUtc",
    cl."Quantity"       AS "Quantity"
FROM "CountingRun" cr
JOIN "CountLine" cl ON cl."CountingRunId" = cr."Id"
JOIN "Product" p ON p."Id" = cl."ProductId"
{InventoryOperatorSqlHelper.AppendJoinClause(operatorSql.JoinClause)}
WHERE cr."LocationId" = @LocationId
  AND cr."CompletedAtUtc" IS NOT NULL
ORDER BY p."Ean", cr."CompletedAtUtc", cr."Id";
""";

        var observationRows = (await connection
                .QueryAsync<SessionConflictObservationRow>(
                    new CommandDefinition(
                        observationSql,
                        new { LocationId = locationId },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false))
            .ToArray();

        const string existingConflictsSql = """
SELECT
    c."Id"               AS "ConflictId",
    c."CountLineId"      AS "CountLineId",
    cl."ProductId"       AS "ProductId",
    c."IsResolved"       AS "IsResolved",
    c."ResolvedQuantity" AS "ResolvedQuantity",
    c."ResolvedAtUtc"    AS "ResolvedAtUtc",
    c."CreatedAtUtc"     AS "CreatedAtUtc"
FROM "Conflict" c
JOIN "CountLine" cl ON cl."Id" = c."CountLineId"
JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
WHERE cr."LocationId" = @LocationId;
""";

        var existingConflicts = (await connection
                .QueryAsync<ExistingConflictRow>(
                    new CommandDefinition(
                        existingConflictsSql,
                        new { LocationId = locationId },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false))
            .ToArray();

        if (observationRows.Length == 0 && existingConflicts.Length == 0)
        {
            return;
        }

        var conflictsLookup = existingConflicts
            .GroupBy(row => row.ProductId)
            .ToDictionary(group => group.Key, group => group.OrderBy(r => r.CreatedAtUtc).ToList(), EqualityComparer<Guid>.Default);

        var deleteIds = new List<Guid>();
        var inserts = new List<ConflictInsertCommand>();
        var reopenUpdates = new List<ConflictOpenUpdate>();
        var resolveUpdates = new List<ConflictResolveUpdate>();

        foreach (var grouping in observationRows.GroupBy(row => row.ProductId))
        {
            var productRows = grouping
                .OrderBy(row => row.CompletedAtUtc)
                .ThenBy(row => row.RunId)
                .ToList();

            if (productRows.Count == 0)
            {
                continue;
            }

            var productId = grouping.Key;
            var productRef = Normalize(productRows[0].Ean) ?? string.Empty;
            var existing = conflictsLookup.TryGetValue(productId, out var existingList)
                ? existingList
                : new List<ExistingConflictRow>();

            if (existing.Count > 1)
            {
                deleteIds.AddRange(existing.Skip(1).Select(row => row.ConflictId));
                existing = new List<ExistingConflictRow> { existing[0] };
            }

            var existingConflict = existing.FirstOrDefault();

            if (existingConflict is not null && existingConflict.IsResolved)
            {
                continue;
            }

            var observationModels = productRows
                .Select(row => new SessionConflictObservation
                {
                    RunId = row.RunId,
                    CountLineId = row.CountLineId,
                    CountType = row.CountType,
                    CountedByUserId = columnsState.HasOwnerUserId ? row.OwnerUserId : null,
                    CountedByDisplayName = Normalize(row.OperatorDisplayName),
                    CountedAtUtc = row.CompletedAtUtc,
                    Quantity = ToIntQuantity(row.Quantity)
                })
                .ToList();

            if (observationModels.Count < 2)
            {
                if (existingConflict is not null)
                {
                    deleteIds.Add(existingConflict.ConflictId);
                }
                continue;
            }

            var resolution = DetectResolution(productRows);
            if (resolution is not null)
            {
                var resolvedQuantity = resolution.Quantity;
                var resolvedAtUtc = resolution.ResolvedAtUtc;

                if (existingConflict is not null)
                {
                    resolveUpdates.Add(new ConflictResolveUpdate(existingConflict.ConflictId, resolution.CountLineId, resolvedQuantity, resolvedAtUtc));
                }
                else
                {
                    inserts.Add(new ConflictInsertCommand(Guid.NewGuid(), resolution.CountLineId, "resolved", resolvedAtUtc, resolvedAtUtc, resolvedQuantity, true));
                }

                continue;
            }

            var distinctQuantities = observationModels
                .Select(obs => obs.Quantity)
                .Distinct()
                .Count();

            if (distinctQuantities <= 1)
            {
                if (existingConflict is not null)
                {
                    deleteIds.Add(existingConflict.ConflictId);
                }

                continue;
            }

            var latestObservation = observationModels
                .OrderByDescending(obs => obs.CountedAtUtc)
                .ThenByDescending(obs => obs.RunId)
                .First();

            if (existingConflict is not null)
            {
                reopenUpdates.Add(new ConflictOpenUpdate(existingConflict.ConflictId, latestObservation.CountLineId));
            }
            else
            {
                inserts.Add(new ConflictInsertCommand(Guid.NewGuid(), latestObservation.CountLineId, "open", latestObservation.CountedAtUtc, null, null, false));
            }
        }

        if (deleteIds.Count > 0)
        {
            const string deleteSql = """
DELETE FROM "Conflict"
WHERE "Id" = ANY(@Ids::uuid[]);
""";
            await connection.ExecuteAsync(
                    new CommandDefinition(deleteSql, new { Ids = deleteIds.ToArray() }, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        foreach (var insert in inserts)
        {
            const string insertSql = """
INSERT INTO "Conflict" ("Id", "CountLineId", "Status", "Notes", "CreatedAtUtc", "ResolvedAtUtc", "ResolvedQuantity", "IsResolved")
VALUES (@Id, @CountLineId, @Status, NULL, @CreatedAtUtc, @ResolvedAtUtc, @ResolvedQuantity, @IsResolved);
""";
            await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertSql,
                        new
                        {
                            Id = insert.ConflictId,
                            insert.CountLineId,
                            insert.Status,
                            CreatedAtUtc = insert.CreatedAtUtc,
                            ResolvedAtUtc = insert.ResolvedAtUtc,
                            ResolvedQuantity = insert.ResolvedQuantity,
                            insert.IsResolved
                        },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        foreach (var update in reopenUpdates)
        {
            const string reopenSql = """
UPDATE "Conflict"
SET "CountLineId" = @CountLineId,
    "Status" = 'open',
    "Notes" = NULL,
    "ResolvedAtUtc" = NULL,
    "ResolvedQuantity" = NULL,
    "IsResolved" = FALSE
WHERE "Id" = @ConflictId;
""";
            await connection.ExecuteAsync(
                    new CommandDefinition(
                        reopenSql,
                        new { update.ConflictId, update.CountLineId },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        foreach (var update in resolveUpdates)
        {
            const string resolveSql = """
UPDATE "Conflict"
SET "CountLineId" = @CountLineId,
    "Status" = 'resolved',
    "ResolvedAtUtc" = @ResolvedAtUtc,
    "ResolvedQuantity" = @ResolvedQuantity,
    "IsResolved" = TRUE
WHERE "Id" = @ConflictId;
""";
            await connection.ExecuteAsync(
                    new CommandDefinition(
                        resolveSql,
                        new
                        {
                            update.ConflictId,
                            update.CountLineId,
                            update.ResolvedAtUtc,
                            update.ResolvedQuantity
                        },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }
    }

    private async Task<SessionConflictResolutionResult> ResolveConflictsForSessionInternalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = new SessionConflictResolutionResult
        {
            SessionId = sessionId,
            SessionExists = false
        };

        const string sessionExistsSql = """
SELECT "Id"
FROM "InventorySession"
WHERE "Id" = @SessionId
LIMIT 1;
""";

        var sessionIdentifier = await connection
            .QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(
                    sessionExistsSql,
                    new { SessionId = sessionId },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (sessionIdentifier is null)
        {
            return result;
        }

        result.SessionExists = true;

        const string sessionLocationSql = """
SELECT "LocationId"
FROM "CountingRun"
WHERE "InventorySessionId" = @SessionId
LIMIT 1;
""";

        var locationId = await connection
            .QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(
                    sessionLocationSql,
                    new { SessionId = sessionId },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (!locationId.HasValue)
        {
            result.Conflicts = Array.Empty<SessionConflictItem>();
            result.Resolved = Array.Empty<SessionResolvedConflictItem>();
            return result;
        }

        await ResolveConflictsForLocationInternalAsync(connection, transaction, locationId.Value, cancellationToken).ConfigureAwait(false);

        var columnsState = await InventoryOperatorSqlHelper
            .DetectOperatorColumnsAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        var operatorSql = InventoryOperatorSqlHelper.BuildOperatorSqlFragments("cr", "owner", columnsState);

        var observationSql = $"""
SELECT
    p."Id"              AS "ProductId",
    p."Ean"             AS "Ean",
    p."Sku"             AS "Sku",
    p."Name"            AS "Name",
    cl."Id"             AS "CountLineId",
    cl."CountingRunId"  AS "RunId",
    cr."InventorySessionId" AS "InventorySessionId",
    cr."CountType"      AS "CountType",
    {operatorSql.OwnerUserIdProjection} AS "OwnerUserId",
    {operatorSql.Projection}            AS "OperatorDisplayName",
    cr."CompletedAtUtc" AS "CompletedAtUtc",
    cl."Quantity"       AS "Quantity"
FROM "CountingRun" cr
JOIN "CountLine" cl ON cl."CountingRunId" = cr."Id"
JOIN "Product" p ON p."Id" = cl."ProductId"
{InventoryOperatorSqlHelper.AppendJoinClause(operatorSql.JoinClause)}
WHERE cr."InventorySessionId" = @SessionId
  AND cr."CompletedAtUtc" IS NOT NULL
ORDER BY p."Ean", cr."CompletedAtUtc", cr."Id";
""";

        var observationRows = (await connection
                .QueryAsync<SessionConflictObservationRow>(
                    new CommandDefinition(
                        observationSql,
                        new { SessionId = sessionId },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false))
            .ToArray();

        if (observationRows.Length == 0)
        {
            result.Conflicts = Array.Empty<SessionConflictItem>();
            result.Resolved = Array.Empty<SessionResolvedConflictItem>();
            return result;
        }

        const string conflictsSql = """
SELECT
    c."Id"               AS "ConflictId",
    c."CountLineId"      AS "CountLineId",
    cl."ProductId"       AS "ProductId",
    c."IsResolved"       AS "IsResolved",
    c."ResolvedQuantity" AS "ResolvedQuantity",
    c."ResolvedAtUtc"    AS "ResolvedAtUtc",
    c."CreatedAtUtc"     AS "CreatedAtUtc"
FROM "Conflict" c
JOIN "CountLine" cl ON cl."Id" = c."CountLineId"
JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
WHERE cr."LocationId" = @LocationId;
""";

        var conflictRows = (await connection
                .QueryAsync<ExistingConflictRow>(
                    new CommandDefinition(
                        conflictsSql,
                        new { LocationId = locationId.Value },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false))
            .ToDictionary(row => row.ProductId, row => row, EqualityComparer<Guid>.Default);

        var conflicts = new List<SessionConflictItem>();
        var resolvedItems = new List<SessionResolvedConflictItem>();

        foreach (var grouping in observationRows.GroupBy(row => row.ProductId))
        {
            if (!conflictRows.TryGetValue(grouping.Key, out var conflict))
            {
                continue;
            }

            var sampleRow = grouping.First();
            var observations = grouping
                .OrderBy(row => row.CompletedAtUtc)
                .ThenBy(row => row.RunId)
                .Select(row => new SessionConflictObservation
                {
                    RunId = row.RunId,
                    CountLineId = row.CountLineId,
                    CountType = row.CountType,
                    CountedByUserId = columnsState.HasOwnerUserId ? row.OwnerUserId : null,
                    CountedByDisplayName = Normalize(row.OperatorDisplayName),
                    CountedAtUtc = row.CompletedAtUtc,
                    Quantity = ToIntQuantity(row.Quantity)
                })
                .ToArray();

            var variance = ComputeSampleVariance(observations.Select(obs => obs.Quantity).ToArray());
            var productRef = Normalize(sampleRow.Ean) ?? string.Empty;
            var sku = Normalize(sampleRow.Sku) ?? string.Empty;
            var name = Normalize(sampleRow.Name) ?? string.Empty;

            if (!conflict.IsResolved)
            {
                conflicts.Add(new SessionConflictItem
                {
                    ProductId = grouping.Key,
                    ProductRef = productRef,
                    Sku = sku,
                    Name = name,
                    Observations = observations,
                    SampleVariance = variance,
                    ResolvedQuantity = null
                });
                continue;
            }

            if (conflict.ResolvedQuantity is int resolvedQuantity)
            {
                var resolvedAt = conflict.ResolvedAtUtc ?? conflict.CreatedAtUtc;
                resolvedItems.Add(new SessionResolvedConflictItem
                {
                    ProductId = grouping.Key,
                    ProductRef = productRef,
                    Sku = sku,
                    Name = name,
                    ResolvedQuantity = resolvedQuantity,
                    ResolvedAtUtc = resolvedAt,
                    ResolutionRule = "two-matching-counts"
                });
            }
        }

        result.Conflicts = conflicts;
        result.Resolved = resolvedItems;
        return result;
    }
    public async Task<SessionConflictResolutionResult> ResolveConflictsForSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var result = await ResolveConflictsForSessionInternalAsync(connection, transaction, sessionId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static double? ComputeSampleVariance(IReadOnlyList<int> values)
    {
        if (values.Count < 2)
        {
            return null;
        }

        var mean = values.Average();
        var sum = 0.0;

        foreach (var value in values)
        {
            var delta = value - mean;
            sum += delta * delta;
        }

        return sum / (values.Count - 1);
    }

    private static int ToIntQuantity(decimal quantity) =>
        (int)Math.Round(quantity, MidpointRounding.AwayFromZero);

    private static ResolutionCandidate? DetectResolution(IReadOnlyList<SessionConflictObservationRow> observations)
    {
        var seen = new Dictionary<int, Guid>();

        foreach (var observation in observations)
        {
            var quantity = ToIntQuantity(observation.Quantity);
            if (seen.ContainsKey(quantity))
            {
                return new ResolutionCandidate(quantity, observation.CountLineId, observation.CompletedAtUtc);
            }

            seen[quantity] = observation.CountLineId;
        }

        return null;
    }

    private sealed record ConflictInsertCommand(
        Guid ConflictId,
        Guid CountLineId,
        string Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ResolvedAtUtc,
        int? ResolvedQuantity,
        bool IsResolved);

    private sealed record ConflictOpenUpdate(Guid ConflictId, Guid CountLineId);

    private sealed record ConflictResolveUpdate(Guid ConflictId, Guid CountLineId, int ResolvedQuantity, DateTimeOffset ResolvedAtUtc);

    private sealed record ResolutionCandidate(int Quantity, Guid CountLineId, DateTimeOffset ResolvedAtUtc);

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

