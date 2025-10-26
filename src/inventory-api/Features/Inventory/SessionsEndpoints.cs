using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Logging;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Validation;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CineBoutique.Inventory.Api.Features.Inventory;

internal static class SessionsEndpoints
{
    public static IEndpointRouteBuilder MapSessionsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapStartEndpoint(app);
        MapCompleteEndpoint(app);
        MapReleaseEndpoint(app);
        MapRestartEndpoint(app);

        return app;
    }

    private static void MapStartEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/inventories/{locationId:guid}/start", async (
            Guid locationId,
            StartRunRequest request,
            IValidator<StartRunRequest> validator,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return EndpointUtilities.Problem(
                    "Requête invalide",
                    "Le corps de la requête est requis.",
                    StatusCodes.Status400BadRequest);
            }

            var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                return EndpointUtilities.ValidationProblem(validationResult);
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            const string selectLocationSql =
                "SELECT \"Id\", \"ShopId\", \"Code\", \"Label\", \"Disabled\" FROM \"Location\" WHERE \"Id\" = @LocationId AND \"ShopId\" = @ShopId LIMIT 1;";

            var location = await connection
                .QuerySingleOrDefaultAsync<LocationMetadataRow>(
                    new CommandDefinition(
                        selectLocationSql,
                        new { LocationId = locationId, ShopId = request.ShopId },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (location is null)
            {
                return EndpointUtilities.Problem(
                    "Ressource introuvable",
                    "La zone demandée est introuvable.",
                    StatusCodes.Status404NotFound);
            }

            if (location.Disabled)
            {
                return EndpointUtilities.Problem(
                    "Zone désactivée",
                    "La zone demandée est désactivée et ne peut pas démarrer de comptage.",
                    StatusCodes.Status409Conflict);
            }

            var columnsState = await InventoryEndpointSupport.DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);

            if (connection is not NpgsqlConnection npgsqlConnection)
            {
                return Results.Problem(
                    "La connexion à la base de données n'est pas compatible avec PostgreSQL.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            if (!await InventoryEndpointSupport.ValidateUserBelongsToShop(npgsqlConnection, request.OwnerUserId, location.ShopId, cancellationToken).ConfigureAwait(false))
            {
                return InventoryEndpointSupport.BadOwnerUser(request.OwnerUserId, location.ShopId);
            }

            const string selectOwnerDisplayNameSql =
                "SELECT \"DisplayName\" FROM \"ShopUser\" WHERE \"Id\" = @OwnerUserId LIMIT 1;";

            var ownerDisplayName = await connection
                .ExecuteScalarAsync<string?>(
                    new CommandDefinition(selectOwnerDisplayNameSql, new { OwnerUserId = request.OwnerUserId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            ownerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName)
                ? null
                : ownerDisplayName.Trim();

            var shouldPersistOperatorDisplayName = columnsState.HasOperatorDisplayName &&
                                                    !columnsState.OperatorDisplayNameIsNullable;
            var storedOperatorDisplayName = shouldPersistOperatorDisplayName
                ? ownerDisplayName ?? request.OwnerUserId.ToString("D")
                : null;

            var activeOperatorSql = InventoryEndpointSupport.BuildOperatorSqlFragments("cr", "owner", columnsState);

            var selectActiveSql = $@"SELECT
    cr.""Id""                AS ""RunId"",
    cr.""InventorySessionId"" AS ""InventorySessionId"",
    cr.""StartedAtUtc""       AS ""StartedAtUtc"",
    {(columnsState.HasOwnerUserId ? "cr.\"OwnerUserId\"" : "NULL::uuid")} AS ""OwnerUserId"",
    {activeOperatorSql.Projection} AS ""OperatorDisplayName""
FROM ""CountingRun"" cr
{InventoryEndpointSupport.AppendJoinClause(activeOperatorSql.JoinClause)}
WHERE cr.""LocationId"" = @LocationId
  AND cr.""CountType"" = @CountType
  AND cr.""CompletedAtUtc"" IS NULL
ORDER BY cr.""StartedAtUtc"" DESC
LIMIT 1;";

            var existingRun = await connection
                .QuerySingleOrDefaultAsync<ActiveCountingRunRow?>(
                    new CommandDefinition(
                        selectActiveSql,
                        new { LocationId = locationId, CountType = request.CountType },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (existingRun is { } active)
            {
                if (columnsState.HasOwnerUserId && active.OwnerUserId is Guid ownerId && ownerId != request.OwnerUserId)
                {
                    var ownerLabel = active.OperatorDisplayName ?? "un autre utilisateur";
                    return EndpointUtilities.Problem(
                        "Conflit",
                        $"Comptage déjà en cours par {ownerLabel}.",
                        StatusCodes.Status409Conflict);
                }

                if (!columnsState.HasOwnerUserId && !string.IsNullOrWhiteSpace(active.OperatorDisplayName) &&
                    !string.IsNullOrWhiteSpace(ownerDisplayName) &&
                    !string.Equals(active.OperatorDisplayName.Trim(), ownerDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    return EndpointUtilities.Problem(
                        "Conflit",
                        $"Comptage déjà en cours par {active.OperatorDisplayName}.",
                        StatusCodes.Status409Conflict);
                }

                return Results.Ok(new StartInventoryRunResponse
                {
                    RunId = active.RunId,
                    InventorySessionId = active.InventorySessionId,
                    LocationId = locationId,
                    CountType = request.CountType,
                    OwnerUserId = columnsState.HasOwnerUserId
                        ? active.OwnerUserId ?? request.OwnerUserId
                        : request.OwnerUserId,
                    OwnerDisplayName = ownerDisplayName,
                    OperatorDisplayName = active.OperatorDisplayName ?? ownerDisplayName,
                    StartedAtUtc = TimeUtil.ToUtcOffset(active.StartedAtUtc)
                });
            }

            if (connection is not DbConnection dbConnection)
            {
                return Results.Problem(
                    "La connexion à la base de données n'est pas compatible avec les transactions.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var transaction = await dbConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var now = DateTimeOffset.UtcNow;

                const string insertSessionSql =
                    "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc);";

                var sessionId = Guid.NewGuid();
                await connection
                    .ExecuteAsync(
                        new CommandDefinition(
                            insertSessionSql,
                            new
                            {
                                Id = sessionId,
                                Name = $"Session zone {location.Code}",
                                StartedAtUtc = now
                            },
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                var runId = Guid.NewGuid();

                var ownerColumn = columnsState.HasOwnerUserId ? ", \"OwnerUserId\"" : string.Empty;
                var ownerValue = columnsState.HasOwnerUserId ? ", @OwnerUserId" : string.Empty;
                var operatorColumn = shouldPersistOperatorDisplayName ? ", \"OperatorDisplayName\"" : string.Empty;
                var operatorValue = shouldPersistOperatorDisplayName ? ", @OperatorDisplayName" : string.Empty;

                var insertRunSql = $@"INSERT INTO ""CountingRun"" (""Id"", ""InventorySessionId"", ""LocationId"", ""CountType"", ""StartedAtUtc""{ownerColumn}{operatorColumn})
VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAtUtc{ownerValue}{operatorValue});";

                var insertParameters = new
                {
                    Id = runId,
                    SessionId = sessionId,
                    LocationId = locationId,
                    CountType = request.CountType,
                    StartedAtUtc = now,
                    OwnerUserId = request.OwnerUserId,
                    OperatorDisplayName = storedOperatorDisplayName
                };

                await connection
                    .ExecuteAsync(
                        new CommandDefinition(
                            insertRunSql,
                            insertParameters,
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return Results.Ok(new StartInventoryRunResponse
                {
                    RunId = runId,
                    InventorySessionId = sessionId,
                    LocationId = locationId,
                    CountType = request.CountType,
                    OwnerUserId = request.OwnerUserId,
                    OwnerDisplayName = ownerDisplayName,
                    OperatorDisplayName = ownerDisplayName ?? storedOperatorDisplayName,
                    StartedAtUtc = now
                });
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        })
        .WithName("StartInventoryRun")
        .WithTags("Inventories")
        .Produces<StartInventoryRunResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .WithOpenApi(op =>
        {
            op.Summary = "Démarre un comptage sur une zone donnée.";
            op.Description = "Crée ou reprend un run actif pour une zone, un type de comptage et un opérateur.";
            return op;
        });
    }

    private static void MapCompleteEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/inventories/{locationId:guid}/complete", async (
            Guid locationId,
            CompleteRunRequest request,
            IValidator<CompleteRunRequest> validator,
            IDbConnection connection,
            IAuditLogger auditLogger,
            IClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return EndpointUtilities.Problem(
                    "Requête invalide",
                    "Le corps de la requête est requis.",
                    StatusCodes.Status400BadRequest);
            }

            var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                return EndpointUtilities.ValidationProblem(validationResult);
            }

            var rawItems = request.Items!.ToArray();
            var sanitizedItems = new List<SanitizedCountLine>(rawItems.Length);
            var additionalFailures = new List<ValidationFailure>();
            for (var index = 0; index < rawItems.Length; index++)
            {
                var item = rawItems[index];
                var sanitizedEan = InventoryCodeValidator.Normalize(item.Ean);
                if (sanitizedEan is null)
                {
                    additionalFailures.Add(new ValidationFailure($"items[{index}].ean", "Chaque ligne doit contenir un code produit."));
                    continue;
                }

                if (!InventoryCodeValidator.TryValidate(sanitizedEan, out var eanError))
                {
                    additionalFailures.Add(new ValidationFailure($"items[{index}].ean", eanError));
                    continue;
                }

                if (item.Quantity < 0)
                {
                    additionalFailures.Add(new ValidationFailure($"items[{index}].quantity", $"La quantité pour le code {sanitizedEan} doit être positive ou nulle."));
                    continue;
                }

                sanitizedItems.Add(new SanitizedCountLine(sanitizedEan, item.Quantity, item.IsManual));
            }

            if (additionalFailures.Count > 0)
            {
                return EndpointUtilities.ValidationProblem(new ValidationResult(additionalFailures));
            }

            if (sanitizedItems.Count == 0)
            {
                return EndpointUtilities.Problem(
                    "Requête invalide",
                    "Au moins une ligne de comptage doit être fournie.",
                    StatusCodes.Status400BadRequest);
            }

            var countType = request.CountType;

            var aggregatedItems = sanitizedItems
                .GroupBy(line => line.Ean, StringComparer.Ordinal)
                .Select(group => new SanitizedCountLine(group.Key, group.Sum(line => line.Quantity), group.Any(line => line.IsManual)))
                .ToList();

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var columnsState = await InventoryEndpointSupport.DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);

            const string selectLocationSql =
                "SELECT \"Id\", \"ShopId\", \"Code\", \"Label\", \"Disabled\" FROM \"Location\" WHERE \"Id\" = @LocationId LIMIT 1;";

            var location = await connection
                .QuerySingleOrDefaultAsync<LocationMetadataRow>(
                    new CommandDefinition(selectLocationSql, new { LocationId = locationId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (location is null)
            {
                return EndpointUtilities.Problem(
                    "Ressource introuvable",
                    "La zone demandée est introuvable.",
                    StatusCodes.Status404NotFound);
            }

            if (location.Disabled)
            {
                return EndpointUtilities.Problem(
                    "Zone désactivée",
                    "La zone demandée est désactivée et ne peut pas être clôturée.",
                    StatusCodes.Status409Conflict);
            }

            if (connection is not NpgsqlConnection npgsqlConnection)
            {
                return Results.Problem(
                    "La connexion à la base de données n'est pas compatible avec PostgreSQL.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            if (!await InventoryEndpointSupport.ValidateUserBelongsToShop(npgsqlConnection, request.OwnerUserId, location.ShopId, cancellationToken).ConfigureAwait(false))
            {
                return InventoryEndpointSupport.BadOwnerUser(request.OwnerUserId, location.ShopId);
            }

            const string selectOwnerDisplayNameSql =
                "SELECT \"DisplayName\" FROM \"ShopUser\" WHERE \"Id\" = @OwnerUserId LIMIT 1;";

            var ownerDisplayName = await connection
                .ExecuteScalarAsync<string?>(
                    new CommandDefinition(selectOwnerDisplayNameSql, new { OwnerUserId = request.OwnerUserId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            ownerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName)
                ? null
                : ownerDisplayName.Trim();

            var shouldPersistOperatorDisplayName = columnsState.HasOperatorDisplayName &&
                                                    !columnsState.OperatorDisplayNameIsNullable;
            var storedOperatorDisplayName = shouldPersistOperatorDisplayName
                ? ownerDisplayName ?? request.OwnerUserId.ToString("D")
                : null;

            if (connection is not DbConnection dbConnection)
            {
                return Results.Problem(
                    "La connexion à la base de données n'est pas compatible avec les transactions.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var transaction = await dbConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var runOperatorSql = InventoryEndpointSupport.BuildOperatorSqlFragments("cr", "owner", columnsState);

                var selectRunSql = $@"SELECT
    cr.""Id""                             AS ""Id"",
    l.""ShopId""                          AS ""ShopId"",
    cr.""InventorySessionId""             AS ""InventorySessionId"",
    cr.""LocationId""                     AS ""LocationId"",
    cr.""CountType""                      AS ""CountType"",
    {(columnsState.HasOwnerUserId ? "cr.\"OwnerUserId\"" : "NULL::uuid")} AS ""OwnerUserId"",
    {runOperatorSql.Projection}       AS ""OperatorDisplayName"",
    CASE
        WHEN cr.""CompletedAtUtc"" IS NOT NULL THEN @StatusCompleted
        WHEN cr.""StartedAtUtc""   IS NOT NULL THEN @StatusInProgress
        ELSE @StatusNotStarted
    END                                 AS ""Status"",
    COALESCE(lines.""LinesCount"", 0)::int           AS ""LinesCount"",
    COALESCE(lines.""TotalQuantity"", 0)::numeric    AS ""TotalQuantity"",
    cr.""StartedAtUtc""                  AS ""StartedAtUtc"",
    cr.""CompletedAtUtc""                AS ""CompletedAtUtc"",
    NULL::timestamptz                  AS ""ReleasedAtUtc""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
{InventoryEndpointSupport.AppendJoinClause(runOperatorSql.JoinClause)}
LEFT JOIN (
    SELECT ""CountingRunId"",
           COUNT(*)::int                 AS ""LinesCount"",
           COALESCE(SUM(""Quantity""), 0)::numeric AS ""TotalQuantity""
    FROM ""CountLine""
    GROUP BY ""CountingRunId""
) AS lines ON lines.""CountingRunId"" = cr.""Id""
WHERE cr.""Id"" = @RunId
LIMIT 1;";

                CountingRunDto? existingRun = null;
                if (request.RunId is { } runId)
                {
                    existingRun = await connection
                        .QuerySingleOrDefaultAsync<CountingRunDto>(
                            new CommandDefinition(
                                selectRunSql,
                                new
                                {
                                    RunId = runId,
                                    StatusCompleted = LocationCountStatus.Completed,
                                    StatusInProgress = LocationCountStatus.InProgress,
                                    StatusNotStarted = LocationCountStatus.NotStarted
                                },
                                transaction,
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false);

                    if (existingRun is null)
                    {
                        return EndpointUtilities.Problem(
                            "Ressource introuvable",
                            "Le run fourni est introuvable.",
                            StatusCodes.Status404NotFound);
                    }

                    if (existingRun.LocationId != locationId)
                    {
                        return EndpointUtilities.Problem(
                            "Requête invalide",
                            "Le run ne correspond pas à la zone demandée.",
                            StatusCodes.Status400BadRequest);
                    }

                    if (existingRun.OwnerUserId is Guid ownerId && ownerId != request.OwnerUserId)
                    {
                        return EndpointUtilities.Problem(
                            "Conflit",
                            "Le run est attribué à un autre opérateur.",
                            StatusCodes.Status409Conflict);
                    }
                }

                if (countType == 2)
                {
                    if (columnsState.HasOwnerUserId)
                    {
                        const string selectFirstRunOwnerSql = @"
SELECT ""OwnerUserId""
FROM ""CountingRun""
WHERE ""LocationId"" = @LocationId
  AND ""CountType"" = 1
  AND ""CompletedAtUtc"" IS NOT NULL
ORDER BY ""CompletedAtUtc"" DESC
LIMIT 1;";

                        var firstRunOwner = await connection
                            .ExecuteScalarAsync<Guid?>(
                                new CommandDefinition(
                                    selectFirstRunOwnerSql,
                                    new { LocationId = locationId },
                                    transaction,
                                    cancellationToken: cancellationToken))
                            .ConfigureAwait(false);

                        if (firstRunOwner is Guid ownerId && ownerId == request.OwnerUserId)
                        {
                            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                            return EndpointUtilities.Problem(
                                "Conflit",
                                "Le deuxième comptage doit être réalisé par un opérateur différent du premier.",
                                StatusCodes.Status409Conflict);
                        }
                    }
                    else if (columnsState.HasOperatorDisplayName)
                    {
                        const string selectFirstRunOperatorSql = @"
SELECT ""OperatorDisplayName""
FROM ""CountingRun""
WHERE ""LocationId"" = @LocationId
  AND ""CountType"" = 1
  AND ""CompletedAtUtc"" IS NOT NULL
ORDER BY ""CompletedAtUtc"" DESC
LIMIT 1;";

                        var firstRunOperator = await connection
                            .ExecuteScalarAsync<string?>(
                                new CommandDefinition(
                                    selectFirstRunOperatorSql,
                                    new { LocationId = locationId },
                                    transaction,
                                    cancellationToken: cancellationToken))
                            .ConfigureAwait(false);

                        if (!string.IsNullOrWhiteSpace(firstRunOperator) &&
                            !string.IsNullOrWhiteSpace(ownerDisplayName) &&
                            string.Equals(firstRunOperator.Trim(), ownerDisplayName, StringComparison.OrdinalIgnoreCase))
                        {
                            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                            return EndpointUtilities.Problem(
                                "Conflit",
                                "Le deuxième comptage doit être réalisé par un opérateur différent du premier.",
                                StatusCodes.Status409Conflict);
                        }
                    }
                }

                var now = clock.UtcNow;

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

                    const string insertSessionSql =
                        "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc);";

                    await connection
                        .ExecuteAsync(
                            new CommandDefinition(
                                insertSessionSql,
                                new
                                {
                                    Id = inventorySessionId,
                                    Name = $"Session zone {location.Code}",
                                    StartedAtUtc = now
                                },
                                transaction,
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false);

                    var ownerColumn = columnsState.HasOwnerUserId ? ", \"OwnerUserId\"" : string.Empty;
                    var ownerValue = columnsState.HasOwnerUserId ? ", @OwnerUserId" : string.Empty;
                    var operatorColumn = shouldPersistOperatorDisplayName ? ", \"OperatorDisplayName\"" : string.Empty;
                    var operatorValue = shouldPersistOperatorDisplayName ? ", @OperatorDisplayName" : string.Empty;

                    var insertRunSql = $@"INSERT INTO ""CountingRun"" (""Id"", ""InventorySessionId"", ""LocationId"", ""CountType"", ""StartedAtUtc"", ""CompletedAtUtc""{ownerColumn}{operatorColumn})
VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAtUtc, @CompletedAtUtc{ownerValue}{operatorValue});";

                    await connection
                        .ExecuteAsync(
                            new CommandDefinition(
                                insertRunSql,
                                new
                                {
                                    Id = countingRunId,
                                    SessionId = inventorySessionId,
                                    LocationId = locationId,
                                    CountType = countType,
                                    StartedAtUtc = now,
                                    CompletedAtUtc = now,
                                    request.OwnerUserId,
                                    OperatorDisplayName = storedOperatorDisplayName
                                },
                                transaction,
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false);
                }

                const string updateSessionSql =
                    "UPDATE \"InventorySession\" SET \"CompletedAtUtc\" = @CompletedAtUtc WHERE \"Id\" = @SessionId;";

                await connection
                    .ExecuteAsync(new CommandDefinition(updateSessionSql, new { SessionId = inventorySessionId, CompletedAtUtc = now }, transaction, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                var ownerUpdateFragment = columnsState.HasOwnerUserId ? ", \"OwnerUserId\" = @OwnerUserId" : string.Empty;
                var operatorUpdateFragment = shouldPersistOperatorDisplayName ? ", \"OperatorDisplayName\" = @OperatorDisplayName" : string.Empty;

                var updateRunSql = $@"UPDATE ""CountingRun""
SET ""CountType"" = @CountType,
    ""CompletedAtUtc"" = @CompletedAtUtc{ownerUpdateFragment}{operatorUpdateFragment}
WHERE ""Id"" = @RunId;";

                var updateRunParameters = new
                {
                    RunId = countingRunId,
                    CountType = countType,
                    CompletedAtUtc = now,
                    request.OwnerUserId,
                    OperatorDisplayName = storedOperatorDisplayName
                };

                await connection
                    .ExecuteAsync(
                        new CommandDefinition(
                            updateRunSql,
                            updateRunParameters,
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                var requestedEans = aggregatedItems.Select(item => item.Ean).Distinct(StringComparer.Ordinal).ToArray();

                const string selectProductsSql = "SELECT \"Id\", \"Ean\", \"CodeDigits\" FROM \"Product\" WHERE \"Ean\" = ANY(@Eans::text[]);";
                var productRows = (await connection
                        .QueryAsync<ProductLookupRow>(
                            new CommandDefinition(selectProductsSql, new { Eans = requestedEans }, transaction, cancellationToken: cancellationToken))
                        .ConfigureAwait(false))
                    .ToList();

                var existingProducts = new Dictionary<string, Guid>(StringComparer.Ordinal);
                foreach (var row in productRows)
                {
                    if (string.IsNullOrWhiteSpace(row.Ean))
                    {
                        continue;
                    }

                    if (existingProducts.ContainsKey(row.Ean))
                    {
                        continue;
                    }

                    existingProducts[row.Ean] = row.Id;
                }

                const string insertProductSql =
                    "INSERT INTO \"Product\" (\"Id\", \"ShopId\", \"Sku\", \"Name\", \"Ean\", \"CodeDigits\", \"CreatedAtUtc\") VALUES (@Id, @ShopId, @Sku, @Name, @Ean, @CodeDigits, @CreatedAtUtc);";

                const string insertLineSql =
                    "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@Id, @RunId, @ProductId, @Quantity, @CountedAtUtc);";

                foreach (var item in aggregatedItems)
                {
                    if (!existingProducts.TryGetValue(item.Ean, out var productId))
                    {
                        productId = Guid.NewGuid();
                        var sku = InventoryEndpointSupport.BuildUnknownSku(item.Ean);
                        var name = $"Produit inconnu EAN {item.Ean}";

                        await connection
                            .ExecuteAsync(
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
                                    cancellationToken: cancellationToken))
                            .ConfigureAwait(false);

                        existingProducts[item.Ean] = productId;
                    }

                    var lineId = Guid.NewGuid();
                    await connection
                        .ExecuteAsync(
                            new CommandDefinition(
                                insertLineSql,
                                new
                                {
                                    Id = lineId,
                                    RunId = countingRunId,
                                    ProductId = productId,
                                    Quantity = item.Quantity,
                                    CountedAtUtc = now
                                },
                                transaction,
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false);
                }

                await InventoryEndpointSupport.ManageConflictsAsync(connection, transaction, locationId, countingRunId, countType, now, cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                var response = new CompleteInventoryRunResponse
                {
                    RunId = countingRunId,
                    InventorySessionId = inventorySessionId,
                    LocationId = locationId,
                    CountType = countType,
                    CompletedAtUtc = now,
                    ItemsCount = aggregatedItems.Count(),
                    TotalQuantity = aggregatedItems.Sum(item => item.Quantity),
                };

                var actor = EndpointUtilities.FormatActorLabel(httpContext);
                var timestamp = EndpointUtilities.FormatTimestamp(now);
                var zoneDescription = string.IsNullOrWhiteSpace(location.Code)
                    ? location.Label
                    : $"{location.Code} – {location.Label}";
                var countDescription = EndpointUtilities.DescribeCountType(countType);
                var auditMessage =
                    $"{actor} a terminé {zoneDescription} pour un {countDescription} le {timestamp} UTC ({response.ItemsCount} références, total {response.TotalQuantity}).";

                await auditLogger.LogAsync(auditMessage, ownerDisplayName, "inventories.complete.success", cancellationToken).ConfigureAwait(false);

                return Results.Ok(response);
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        })
        .WithName("CompleteInventoryRun")
        .WithTags("Inventories")
        .Produces<CompleteInventoryRunResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapReleaseEndpoint(IEndpointRouteBuilder app)
    {
        static async Task<IResult> HandleReleaseAsync(
            Guid locationId,
            ReleaseRunRequest request,
            IDbConnection connection,
            CancellationToken cancellationToken)
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var columnsState = await InventoryEndpointSupport.DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
            var runOperatorSql = InventoryEndpointSupport.BuildOperatorSqlFragments("cr", "owner", columnsState);

            var selectRunSql = $@"
SELECT
    cr.""InventorySessionId"" AS ""InventorySessionId"",
    l.""ShopId""              AS ""ShopId"",
    {(columnsState.HasOwnerUserId ? "cr.\"OwnerUserId\"" : "NULL::uuid")} AS ""OwnerUserId"",
    {runOperatorSql.Projection} AS ""OperatorDisplayName""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
{InventoryEndpointSupport.AppendJoinClause(runOperatorSql.JoinClause)}
WHERE cr.""Id"" = @RunId
  AND cr.""LocationId"" = @LocationId
  AND cr.""CompletedAtUtc"" IS NULL
LIMIT 1;";

            var run = await connection
                .QuerySingleOrDefaultAsync<(Guid InventorySessionId, Guid ShopId, Guid? OwnerUserId, string? OperatorDisplayName)?> (
                    new CommandDefinition(
                        selectRunSql,
                        new { RunId = request.RunId, LocationId = locationId },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (run is null)
            {
                return EndpointUtilities.Problem(
                    "Ressource introuvable",
                    "Aucun comptage actif pour les critères fournis.",
                    StatusCodes.Status404NotFound);
            }

            if (connection is NpgsqlConnection npgsqlConnection)
            {
                if (!await InventoryEndpointSupport.ValidateUserBelongsToShop(npgsqlConnection, request.OwnerUserId, run.Value.ShopId, cancellationToken).ConfigureAwait(false))
                {
                    return InventoryEndpointSupport.BadOwnerUser(request.OwnerUserId, run.Value.ShopId);
                }
            }

            if (columnsState.HasOwnerUserId)
            {
                if (run.Value.OwnerUserId is Guid ownerId && ownerId != request.OwnerUserId)
                {
                    var ownerLabel = run.Value.OperatorDisplayName ?? "un autre utilisateur";
                    return EndpointUtilities.Problem(
                        "Conflit",
                        $"Comptage détenu par {ownerLabel}.",
                        StatusCodes.Status409Conflict);
                }
            }
            else if (columnsState.HasOperatorDisplayName)
            {
                const string selectOwnerDisplayNameSql =
                    "SELECT \"DisplayName\" FROM \"ShopUser\" WHERE \"Id\" = @OwnerUserId LIMIT 1;";

                var requestedDisplayName = await connection
                    .ExecuteScalarAsync<string?>(
                        new CommandDefinition(selectOwnerDisplayNameSql, new { OwnerUserId = request.OwnerUserId }, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                var existingOperator = run.Value.OperatorDisplayName?.Trim();
                if (!string.IsNullOrWhiteSpace(existingOperator) &&
                    !string.IsNullOrWhiteSpace(requestedDisplayName) &&
                    !string.Equals(existingOperator, requestedDisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return EndpointUtilities.Problem(
                        "Conflit",
                        $"Comptage détenu par {existingOperator}.",
                        StatusCodes.Status409Conflict);
                }
            }

            if (connection is not DbConnection dbConnection)
            {
                return Results.Problem(
                    "La connexion à la base de données n'est pas compatible avec les transactions.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var transaction = await dbConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                const string countLinesSql =
                    "SELECT COUNT(*)::int FROM \"CountLine\" WHERE \"CountingRunId\" = @RunId";

                var lineCount = await connection
                    .ExecuteScalarAsync<int>(
                        new CommandDefinition(countLinesSql, new { RunId = request.RunId }, transaction, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                if (lineCount > 0)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return EndpointUtilities.Problem(
                        "Conflit",
                        "Impossible de libérer un comptage contenant des lignes enregistrées.",
                        StatusCodes.Status409Conflict);
                }

                const string deleteRunSql = "DELETE FROM \"CountingRun\" WHERE \"Id\" = @RunId;";
                await connection
                    .ExecuteAsync(
                        new CommandDefinition(deleteRunSql, new { RunId = request.RunId }, transaction, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                const string countSessionRunsSql =
                    "SELECT COUNT(*)::int FROM \"CountingRun\" WHERE \"InventorySessionId\" = @SessionId;";

                var remainingRuns = await connection
                    .ExecuteScalarAsync<int>(
                        new CommandDefinition(
                            countSessionRunsSql,
                            new { SessionId = run.Value.InventorySessionId },
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                if (remainingRuns == 0)
                {
                    const string deleteSessionSql = "DELETE FROM \"InventorySession\" WHERE \"Id\" = @SessionId;";
                    await connection
                        .ExecuteAsync(
                            new CommandDefinition(
                                deleteSessionSql,
                                new { SessionId = run.Value.InventorySessionId },
                                transaction,
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }

        app.MapPost("/api/inventories/{locationId:guid}/release", async (
            Guid locationId,
            ReleaseRunRequest request,
            IValidator<ReleaseRunRequest> validator,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return EndpointUtilities.Problem(
                    "Requête invalide",
                    "Le corps de la requête est requis.",
                    StatusCodes.Status400BadRequest);
            }

            var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                return EndpointUtilities.ValidationProblem(validationResult);
            }

            return await HandleReleaseAsync(locationId, request, connection, cancellationToken).ConfigureAwait(false);
        })
        .WithName("ReleaseInventoryRun")
        .WithTags("Inventories")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .WithOpenApi(op =>
        {
            op.Summary = "Libère un comptage en cours sans le finaliser.";
            op.Description = "Supprime le run actif lorsqu'aucune ligne n'a été enregistrée, ce qui libère la zone.";
            return op;
        });

        app.MapDelete("/api/inventories/{locationId:guid}/runs/{runId:guid}", async (
            Guid locationId,
            Guid runId,
            Guid ownerUserId,
            IValidator<ReleaseRunRequest> validator,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            var request = new ReleaseRunRequest(runId, ownerUserId);

            var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                return EndpointUtilities.ValidationProblem(validationResult);
            }

            return await HandleReleaseAsync(locationId, request, connection, cancellationToken).ConfigureAwait(false);
        })
        .WithName("AbortInventoryRun")
        .WithTags("Inventories")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .WithOpenApi(op =>
        {
            op.Summary = "Libère un comptage via la route historique DELETE.";
            op.Description = "Route de compatibilité acceptant ownerUserId en paramètre de requête pour libérer un run actif.";
            return op;
        });
    }

    private static void MapRestartEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/inventories/{locationId:guid}/restart", async (
            Guid locationId,
            RestartRunRequest request,
            IValidator<RestartRunRequest> validator,
            IDbConnection connection,
            IAuditLogger auditLogger,
            IClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return EndpointUtilities.Problem(
                    "Requête invalide",
                    "Le corps de la requête est requis.",
                    StatusCodes.Status400BadRequest);
            }

            var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                return EndpointUtilities.ValidationProblem(validationResult);
            }

            var countType = request.CountType;

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            const string selectLocationSql =
                "SELECT \"Id\", \"ShopId\", \"Code\", \"Label\", \"Disabled\" FROM \"Location\" WHERE \"Id\" = @LocationId LIMIT 1;";

            var location = await connection
                .QuerySingleOrDefaultAsync<LocationMetadataRow>(
                    new CommandDefinition(selectLocationSql, new { LocationId = locationId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (location is null)
            {
                return EndpointUtilities.Problem(
                    "Ressource introuvable",
                    "La zone demandée est introuvable.",
                    StatusCodes.Status404NotFound);
            }

            if (location.Disabled)
            {
                return EndpointUtilities.Problem(
                    "Zone désactivée",
                    "La zone demandée est désactivée et ne peut pas être relancée.",
                    StatusCodes.Status409Conflict);
            }

            if (connection is NpgsqlConnection npgsqlConnection)
            {
                if (!await InventoryEndpointSupport.ValidateUserBelongsToShop(npgsqlConnection, request.OwnerUserId, location.ShopId, cancellationToken).ConfigureAwait(false))
                {
                    return InventoryEndpointSupport.BadOwnerUser(request.OwnerUserId, location.ShopId);
                }
            }

            const string sql = @"UPDATE ""CountingRun""
SET ""CompletedAtUtc"" = @NowUtc
WHERE ""LocationId"" = @LocationId
  AND ""CompletedAtUtc"" IS NULL
  AND ""CountType"" = @CountType;";

            var now = clock.UtcNow;
            var affected = await connection
                .ExecuteAsync(new CommandDefinition(sql, new { LocationId = locationId, CountType = countType, NowUtc = now }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
            var actor = EndpointUtilities.FormatActorLabel(httpContext);
            var timestamp = EndpointUtilities.FormatTimestamp(now);
            var zoneDescription = !string.IsNullOrWhiteSpace(location.Code)
                ? $"la zone {location.Code} – {location.Label}"
                : !string.IsNullOrWhiteSpace(location.Label)
                    ? $"la zone {location.Label}"
                    : $"la zone {locationId}";
            var countDescription = EndpointUtilities.DescribeCountType(countType);
            var resultDetails = affected > 0 ? "et clôturé les comptages actifs" : "mais aucun comptage actif n'était ouvert";
            var message = $"{actor} a relancé {zoneDescription} pour un {countDescription} le {timestamp} UTC {resultDetails}.";

            await auditLogger.LogAsync(message, userName, "inventories.restart", cancellationToken).ConfigureAwait(false);

            return Results.NoContent();
        })
        .WithName("RestartInventoryForLocation")
        .WithTags("Inventories")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .WithOpenApi(op =>
        {
            op.Summary = "Force la clôture des comptages actifs pour une zone et un type donnés.";
            op.Description = "Permet de terminer les runs ouverts sur une zone pour relancer un nouveau comptage.";
            return op;
        });
    }
}

internal static class InventoryEndpointSupport
{
    internal static string BuildUnknownSku(string ean)
    {
        if (string.IsNullOrWhiteSpace(ean))
        {
            return $"UNK-{Guid.NewGuid():N}"[..32];
        }

        var normalized = InventoryCodeValidator.Normalize(ean) ?? string.Empty;
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

    internal static async Task ManageInitialConflictsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
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

        const string selectCounterpartRunSql = @"SELECT ""Id""
FROM ""CountingRun""
WHERE ""LocationId"" = @LocationId AND ""CountType"" = @Counterpart AND ""CompletedAtUtc"" IS NOT NULL
ORDER BY ""CompletedAtUtc"" DESC
LIMIT 1;";

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

        const string deleteExistingConflictsSql = @"DELETE FROM ""Conflict""
USING ""CountLine"" cl
WHERE ""Conflict"".""CountLineId"" = cl.""Id""
  AND cl.""CountingRunId"" IN (@CurrentRunId, @CounterpartRunId)
  AND ""Conflict"".""ResolvedAtUtc"" IS NULL;";

        await connection.ExecuteAsync(
                new CommandDefinition(
                    deleteExistingConflictsSql,
                    new { CurrentRunId = currentRunId, CounterpartRunId = counterpartRunId.Value },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var runIds = new[] { currentRunId, counterpartRunId.Value };

        const string aggregateSql = @"SELECT
    cl.""CountingRunId"",
    p.""Ean"",
    p.""Id"" AS ""ProductId"",
    SUM(cl.""Quantity"") AS ""Quantity""
FROM ""CountLine"" cl
JOIN ""Product"" p ON p.""Id"" = cl.""ProductId""
WHERE cl.""CountingRunId"" = ANY(@RunIds::uuid[])
GROUP BY cl.""CountingRunId"", p.""Id"", p.""Ean"";";

        var aggregatedRows = await connection
            .QueryAsync<AggregatedCountRow>(
                new CommandDefinition(
                    aggregateSql,
                    new { RunIds = runIds },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var quantityByRun = BuildQuantityLookup(aggregatedRows);

        const string lineLookupSql = @"SELECT DISTINCT ON (cl.""CountingRunId"", p.""Id"")
    cl.""CountingRunId"",
    cl.""Id"" AS ""CountLineId"",
    p.""Id"" AS ""ProductId"",
    p.""Ean""
FROM ""CountLine"" cl
JOIN ""Product"" p ON p.""Id"" = cl.""ProductId""
WHERE cl.""CountingRunId"" = ANY(@RunIds::uuid[])
ORDER BY cl.""CountingRunId"", p.""Id"", cl.""CountedAtUtc"" DESC, cl.""Id"" DESC;";

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

        const string insertConflictSql =
            "INSERT INTO \"Conflict\" (\"Id\", \"CountLineId\", \"Status\", \"Notes\", \"CreatedAtUtc\") VALUES (@Id, @CountLineId, @Status, @Notes, @CreatedAtUtc);";

        foreach (var payload in conflictInserts)
        {
            await connection.ExecuteAsync(
                    new CommandDefinition(insertConflictSql, payload, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }
    }

    internal static async Task ManageAdditionalConflictsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid locationId,
        Guid currentRunId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string completedRunsSql = @"SELECT ""Id"" FROM ""CountingRun"" WHERE ""LocationId"" = @LocationId AND ""CompletedAtUtc"" IS NOT NULL";

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

        const string aggregateSql = @"SELECT
    cl.""CountingRunId"",
    p.""Ean"",
    p.""Id"" AS ""ProductId"",
    SUM(cl.""Quantity"") AS ""Quantity""
FROM ""CountLine"" cl
JOIN ""Product"" p ON p.""Id"" = cl.""ProductId""
WHERE cl.""CountingRunId"" = ANY(@RunIds::uuid[])
GROUP BY cl.""CountingRunId"", p.""Id"", p.""Ean"";";

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
            return HaveIdenticalQuantities(currentQuantities, otherQuantities);
        });

        const string resolveConflictsSql = @"DELETE FROM ""Conflict""
USING ""CountLine"" cl
JOIN ""CountingRun"" cr ON cr.""Id"" = cl.""CountingRunId""
WHERE ""Conflict"".""CountLineId"" = cl.""Id""
  AND cr.""LocationId"" = @LocationId
  AND ""Conflict"".""ResolvedAtUtc"" IS NULL;";

        if (!hasMatch)
        {
            return;
        }

        await connection.ExecuteAsync(
                new CommandDefinition(
                    resolveConflictsSql,
                    new { LocationId = locationId },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    internal static Dictionary<Guid, Dictionary<string, decimal>> BuildQuantityLookup(IEnumerable<AggregatedCountRow> aggregatedRows)
        => aggregatedRows
            .GroupBy(row => row.CountingRunId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(
                    row => BuildProductKey(row.ProductId, row.Ean),
                    row => row.Quantity,
                    StringComparer.Ordinal),
                EqualityComparer<Guid>.Default);

    internal static bool HaveIdenticalQuantities(
        Dictionary<string, decimal> current,
        Dictionary<string, decimal> reference)
    {
        var keys = current.Keys
            .Union(reference.Keys, StringComparer.Ordinal)
            .ToArray();

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

    internal static Guid? ResolveCountLineId(
        Dictionary<Guid, Dictionary<string, Guid>> lookup,
        Guid currentRunId,
        Guid counterpartRunId,
        string key)
    {
        if (lookup.TryGetValue(currentRunId, out var currentLines) && currentLines.TryGetValue(key, out var currentLineId))
            return currentLineId;

        if (lookup.TryGetValue(counterpartRunId, out var counterpartLines) && counterpartLines.TryGetValue(key, out var counterpartLineId))
            return counterpartLineId;

        return null;
    }

    internal sealed record OperatorColumnsState(bool HasOperatorDisplayName, bool OperatorDisplayNameIsNullable, bool HasOwnerUserId);

    internal sealed record OperatorSqlFragments(
        string Projection,
        string OwnerDisplayProjection,
        string OperatorDisplayProjection,
        string OwnerUserIdProjection,
        string? JoinClause);

    internal static async Task<OperatorColumnsState> DetectOperatorColumnsAsync(
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        var hasOperatorDisplayNameColumn = await EndpointUtilities
            .ColumnExistsAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken)
            .ConfigureAwait(false);

        var operatorDisplayNameIsNullable = hasOperatorDisplayNameColumn && await EndpointUtilities
            .ColumnIsNullableAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken)
            .ConfigureAwait(false);

        var hasOwnerUserIdColumn = await EndpointUtilities
            .ColumnExistsAsync(connection, "CountingRun", "OwnerUserId", cancellationToken)
            .ConfigureAwait(false);

        return new OperatorColumnsState(hasOperatorDisplayNameColumn, operatorDisplayNameIsNullable, hasOwnerUserIdColumn);
    }

    internal static OperatorSqlFragments BuildOperatorSqlFragments(
        string runAlias,
        string ownerAlias,
        OperatorColumnsState state)
    {
        var ownerUserIdProjection = state.HasOwnerUserId
            ? $"{runAlias}.\"OwnerUserId\""
            : "NULL::uuid";

        var operatorDisplayProjection = state.HasOperatorDisplayName
            ? $"{runAlias}.\"OperatorDisplayName\""
            : "NULL::text";

        if (state.HasOwnerUserId)
        {
            var ownerDisplayProjection = $"{ownerAlias}.\"DisplayName\"";
            var joinClause = $"LEFT JOIN \"ShopUser\" {ownerAlias} ON {ownerAlias}.\"Id\" = {runAlias}.\"OwnerUserId\"";
            var projection = $"COALESCE({ownerDisplayProjection}, {operatorDisplayProjection})";
            return new OperatorSqlFragments(
                projection,
                ownerDisplayProjection,
                operatorDisplayProjection,
                ownerUserIdProjection,
                joinClause);
        }

        const string ownerDisplayFallback = "NULL::text";
        var defaultProjection = $"COALESCE({ownerDisplayFallback}, {operatorDisplayProjection})";
        return new OperatorSqlFragments(
            defaultProjection,
            ownerDisplayFallback,
            operatorDisplayProjection,
            ownerUserIdProjection,
            null);
    }

    internal static string AppendJoinClause(string? joinClause) =>
        string.IsNullOrWhiteSpace(joinClause) ? string.Empty : $"\n{joinClause}";

    internal static string BuildProductKey(Guid productId, string? ean)
    {
        if (!string.IsNullOrWhiteSpace(ean))
        {
            return ean.Trim();
        }

        return productId.ToString("D");
    }

    internal static async Task ManageConflictsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
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

        await ManageAdditionalConflictsAsync(connection, transaction, locationId, currentRunId, now, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<bool> ValidateUserBelongsToShop(NpgsqlConnection cn, Guid ownerUserId, Guid shopId, CancellationToken ct)
    {
        const string sql = """
    SELECT 1
    FROM "ShopUser"
    WHERE "Id" = @ownerUserId
      AND "ShopId" = @shopId
      AND NOT "Disabled"
    """;

        var command = new CommandDefinition(sql, new { ownerUserId, shopId }, cancellationToken: ct);

        return await cn.ExecuteScalarAsync<int?>(command).ConfigureAwait(false) is 1;
    }

    internal static IResult BadOwnerUser(Guid ownerUserId, Guid shopId) =>
        EndpointUtilities.Problem(
            "Requête invalide",
            "ownerUserId n'appartient pas à la boutique fournie ou est désactivé.",
            StatusCodes.Status400BadRequest,
            new Dictionary<string, object?>
            {
                [nameof(ownerUserId)] = ownerUserId,
                [nameof(shopId)] = shopId
            });
}

internal sealed record SanitizedCountLine(string Ean, decimal Quantity, bool IsManual);

// Dapper aime bien ça
public sealed class AggregatedCountRow
{
    public Guid CountingRunId { get; set; }
    public string Ean { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }

    public AggregatedCountRow() { }
}

internal sealed record CountLineReference(Guid CountingRunId, Guid CountLineId, Guid ProductId, string? Ean);

