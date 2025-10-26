using System;
using System.Globalization;
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

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
