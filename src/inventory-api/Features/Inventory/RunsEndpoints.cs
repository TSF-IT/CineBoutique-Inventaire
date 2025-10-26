using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure;
using CineBoutique.Inventory.Api.Infrastructure.Logging;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Validation;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Api.Features.Inventory;

internal static class RunsEndpoints
{
    public static IEndpointRouteBuilder MapRunsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapSummaryEndpoint(app);
        MapCompletedRunDetailEndpoint(app);
        MapActiveRunLookupEndpoint(app);

        return app;
    }

    private static void MapSummaryEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inventories/summary", async (
            string? shopId,
            IDbConnection connection,
            [FromServices] ILogger<InventoryEndpointsMarker> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(shopId))
            {
                return Results.Problem(
                    detail: "ShopId requis",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!Guid.TryParse(shopId, out var parsedShopId))
            {
                return Results.Problem(
                    detail: "ShopId invalide",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var hasIsActiveColumn = await EndpointUtilities
                .ColumnExistsAsync(connection, "CountingRun", "IsActive", cancellationToken)
                .ConfigureAwait(false);

            var activitySources = new List<string>
            {
                @"SELECT MAX(cl.""CountedAtUtc"") AS value
        FROM ""CountLine"" cl
        JOIN ""CountingRun"" cr ON cr.""Id"" = cl.""CountingRunId""
        JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
        WHERE l.""ShopId"" = @ShopId",
                @"SELECT MAX(cr.""StartedAtUtc"") AS value
        FROM ""CountingRun"" cr
        JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
        WHERE l.""ShopId"" = @ShopId",
                @"SELECT MAX(cr.""CompletedAtUtc"") AS value
        FROM ""CountingRun"" cr
        JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
        WHERE l.""ShopId"" = @ShopId"
            };

            if (await EndpointUtilities.TableExistsAsync(connection, "Audit", cancellationToken).ConfigureAwait(false))
            {
                activitySources.Insert(0,
                    @"SELECT MAX(a.""CreatedAtUtc"") AS value
        FROM ""Audit"" a
        WHERE EXISTS (
            SELECT 1
            FROM ""CountingRun"" cr
            JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
            WHERE l.""ShopId"" = @ShopId
              AND a.""EntityId"" = cr.""Id""::text
        )");
            }

            var activityUnion = string.Join("\n            UNION ALL\n            ", activitySources);

            var summarySql = $@"SELECT
    (
        SELECT COUNT(DISTINCT cr.""InventorySessionId"")
        FROM ""CountingRun"" cr
        JOIN ""InventorySession"" s ON s.""Id"" = cr.""InventorySessionId""
        JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
        WHERE s.""CompletedAtUtc"" IS NULL
          AND l.""ShopId"" = @ShopId
    ) AS ""ActiveSessions"",
    (
        SELECT MAX(value)
        FROM (
            {activityUnion}
        ) AS activity
    ) AS ""LastActivityUtc"";";

            var summary = await connection
                .QueryFirstOrDefaultAsync<InventorySummaryDto>(
                    new CommandDefinition(summarySql, new { ShopId = parsedShopId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false)
                ?? new InventorySummaryDto();

            var openRunsSql = $@"SELECT
    cr.""Id""           AS ""RunId"",
    cr.""LocationId"",
    l.""Code""          AS ""LocationCode"",
    l.""Label""         AS ""LocationLabel"",
    cr.""CountType"",
    COALESCE(su.""DisplayName"", NULL) AS ""OwnerDisplayName"",
    cr.""OwnerUserId"",
    cr.""StartedAtUtc""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
LEFT JOIN ""ShopUser"" su ON su.""Id"" = cr.""OwnerUserId"" AND su.""ShopId"" = l.""ShopId""
WHERE l.""ShopId"" = @ShopId
  AND cr.""CompletedAtUtc"" IS NULL
  AND EXISTS (SELECT 1 FROM ""CountLine"" cl WHERE cl.""CountingRunId"" = cr.""Id"")
{(hasIsActiveColumn ? "  AND cr.\"IsActive\" = TRUE\n" : string.Empty)}ORDER BY cr.""StartedAtUtc"" DESC;";

            var openRunRows = (await connection
                    .QueryAsync<OpenRunSummaryRow>(
                        new CommandDefinition(openRunsSql, new { ShopId = parsedShopId }, cancellationToken: cancellationToken))
                    .ConfigureAwait(false)).ToList();

            var openRunDetails = openRunRows
                .Select(row => new OpenRunSummaryDto
                {
                    RunId = row.RunId,
                    LocationId = row.LocationId,
                    LocationCode = row.LocationCode,
                    LocationLabel = row.LocationLabel,
                    CountType = row.CountType,
                    OwnerDisplayName = row.OwnerDisplayName,
                    OwnerUserId = row.OwnerUserId,
                    StartedAtUtc = TimeUtil.ToUtcOffset(row.StartedAtUtc)
                })
                .ToList();

            summary.OpenRunDetails = openRunDetails;
            summary.OpenRuns = openRunDetails.Count;

            var completedRunsSql = @"SELECT
    cr.""Id""           AS ""RunId"",
    cr.""LocationId"",
    l.""Code""          AS ""LocationCode"",
    l.""Label""         AS ""LocationLabel"",
    cr.""CountType"",
    COALESCE(su.""DisplayName"", NULL) AS ""OwnerDisplayName"",
    cr.""OwnerUserId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
LEFT JOIN ""ShopUser"" su ON su.""Id"" = cr.""OwnerUserId"" AND su.""ShopId"" = l.""ShopId""
WHERE l.""ShopId"" = @ShopId
  AND cr.""CompletedAtUtc"" IS NOT NULL
ORDER BY cr.""CompletedAtUtc"" DESC
LIMIT 50;";

            var completedRunRows = (await connection
                    .QueryAsync<CompletedRunSummaryRow>(
                        new CommandDefinition(
                            completedRunsSql,
                            new { ShopId = parsedShopId },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false)).ToList();

            var completedRunDetails = completedRunRows
                .Select(row => new CompletedRunSummaryDto
                {
                    RunId = row.RunId,
                    LocationId = row.LocationId,
                    LocationCode = row.LocationCode,
                    LocationLabel = row.LocationLabel,
                    CountType = row.CountType,
                    OwnerDisplayName = row.OwnerDisplayName,
                    OwnerUserId = row.OwnerUserId,
                    StartedAtUtc = TimeUtil.ToUtcOffset(row.StartedAtUtc),
                    CompletedAtUtc = TimeUtil.ToUtcOffset(row.CompletedAtUtc)
                })
                .ToList();

            summary.CompletedRunDetails = completedRunDetails;
            summary.CompletedRuns = completedRunDetails.Count;

            var conflictZoneRows = (await connection
                    .QueryAsync<ConflictZoneSummaryRow>(
                        new CommandDefinition(
                            @"SELECT
    cr.""LocationId"" AS ""LocationId"",
    l.""Code""        AS ""LocationCode"",
    l.""Label""       AS ""LocationLabel"",
    COUNT(*)::int      AS ""ConflictLines""
FROM ""Conflict"" c
JOIN ""CountLine""  cl ON cl.""Id"" = c.""CountLineId""
JOIN ""CountingRun"" cr ON cr.""Id"" = cl.""CountingRunId""
JOIN ""Location""    l ON l.""Id"" = cr.""LocationId""
WHERE c.""ResolvedAtUtc"" IS NULL
  AND l.""ShopId"" = @ShopId
GROUP BY cr.""LocationId"", l.""Code"", l.""Label""
ORDER BY l.""Code"";",
                            new { ShopId = parsedShopId },
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false)).ToList();

            summary.ConflictZones = [.. conflictZoneRows
                .Select(row => new ConflictZoneSummaryDto
                {
                    LocationId = row.LocationId,
                    LocationCode = row.LocationCode,
                    LocationLabel = row.LocationLabel,
                    ConflictLines = row.ConflictLines
                })];

            summary.Conflicts = summary.ConflictZones.Count;

            var summaryQuery = FormattableString.Invariant($"conflicts summary shop={parsedShopId} zones={summary.Conflicts}");
            ApiLog.InventorySearch(logger, summaryQuery);

            return Results.Ok(summary);
        })
        .WithName("GetInventorySummary")
        .WithTags("Inventories")
        .Produces<InventorySummaryDto>(StatusCodes.Status200OK)
        .WithOpenApi(op =>
        {
            op.Summary = "Récupère un résumé des inventaires en cours.";
            op.Description = "Fournit un aperçu synthétique incluant les comptages en cours, les conflits à résoudre et la dernière activité.";
            return op;
        });
    }

    private static void MapCompletedRunDetailEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inventories/runs/{runId:guid}", async (
            Guid runId,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var columnsState = await InventoryEndpointSupport.DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
            var runOperatorSql = InventoryEndpointSupport.BuildOperatorSqlFragments("cr", "owner", columnsState);

            var runSql = $@"SELECT
    cr.""Id""           AS ""RunId"",
    cr.""LocationId"",
    l.""Code""          AS ""LocationCode"",
    l.""Label""         AS ""LocationLabel"",
    cr.""CountType"",
    {runOperatorSql.Projection} AS ""OperatorDisplayName"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
{InventoryEndpointSupport.AppendJoinClause(runOperatorSql.JoinClause)}
WHERE cr.""Id"" = @RunId
  AND cr.""CompletedAtUtc"" IS NOT NULL
LIMIT 1;";

            var runRow = await connection
                .QuerySingleOrDefaultAsync<CompletedRunDetailRow>(
                    new CommandDefinition(runSql, new { RunId = runId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (runRow is null)
            {
                return Results.NotFound();
            }

            const string linesSql = @"SELECT
    cl.""ProductId"" AS ""ProductId"",
    p.""Sku""        AS ""Sku"",
    p.""Name""       AS ""Name"",
    p.""Ean""        AS ""Ean"",
    cl.""Quantity""  AS ""Quantity""
FROM ""CountLine"" cl
JOIN ""Product"" p ON p.""Id"" = cl.""ProductId""
WHERE cl.""CountingRunId"" = @RunId
ORDER BY COALESCE(p.""Ean"", p.""Sku""), p.""Name"";";

            var lineRows = (await connection
                    .QueryAsync<CompletedRunLineRow>(
                        new CommandDefinition(linesSql, new { RunId = runId }, cancellationToken: cancellationToken))
                    .ConfigureAwait(false)).ToList();

            var payload = new CompletedRunDetailDto
            {
                RunId = runRow.RunId,
                LocationId = runRow.LocationId,
                LocationCode = runRow.LocationCode,
                LocationLabel = runRow.LocationLabel,
                CountType = runRow.CountType,
                OperatorDisplayName = runRow.OperatorDisplayName,
                StartedAtUtc = TimeUtil.ToUtcOffset(runRow.StartedAtUtc),
                CompletedAtUtc = TimeUtil.ToUtcOffset(runRow.CompletedAtUtc),
                Items = lineRows
                    .Select(row => new CompletedRunDetailItemDto
                    {
                        ProductId = row.ProductId,
                        Sku = row.Sku,
                        Name = row.Name,
                        Ean = row.Ean,
                        Quantity = row.Quantity
                    })
                    .ToList()
            };

            return Results.Ok(payload);
        })
        .WithName("GetCompletedRunDetail")
        .WithTags("Inventories")
        .Produces<CompletedRunDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi(op =>
        {
            op.Summary = "Récupère le détail d’un comptage terminé.";
            op.Description = "Retourne la liste des lignes scannées pour un comptage clôturé.";
            return op;
        });
    }

    private static void MapActiveRunLookupEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inventories/{locationId:guid}/active-run", async (
            Guid locationId,
            int countType,
            Guid ownerUserId,
            Guid? sessionId,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (countType < 1)
                return Results.BadRequest(new { message = "countType doit être supérieur ou égal à 1." });

            if (ownerUserId == Guid.Empty)
                return Results.BadRequest(new { message = "ownerUserId est requis." });

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            // Résoudre la session cible : paramètre explicite, sinon dernière session non complétée
            Guid targetSessionId;
            if (sessionId is { } sid)
            {
                targetSessionId = sid;
            }
            else
            {
                const string selectActiveSession = @"
    SELECT ""Id""
    FROM ""InventorySession""
    WHERE ""CompletedAtUtc"" IS NULL
    ORDER BY ""StartedAtUtc"" DESC
    LIMIT 1;";
                var resolved = await connection.QuerySingleOrDefaultAsync<Guid?>(
                    new CommandDefinition(selectActiveSession, cancellationToken: cancellationToken)).ConfigureAwait(false);

                if (!resolved.HasValue)
                    return Results.NotFound(new { message = "Aucune session active." });

                targetSessionId = resolved.Value;
            }

            var columnsState = await InventoryEndpointSupport.DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
            var runOperatorSql = InventoryEndpointSupport.BuildOperatorSqlFragments("cr", "owner", columnsState);

            string? ownerDisplayName = null;
            if (columnsState.HasOperatorDisplayName || columnsState.HasOwnerUserId)
            {
                const string selectOwnerDisplayNameSql =
                    "SELECT \"DisplayName\" FROM \"ShopUser\" WHERE \"Id\" = @OwnerUserId LIMIT 1;";

                ownerDisplayName = await connection
                    .ExecuteScalarAsync<string?>(
                        new CommandDefinition(selectOwnerDisplayNameSql, new { OwnerUserId = ownerUserId }, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                ownerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName)
                    ? null
                    : ownerDisplayName.Trim();
            }

            string selectRunSql;
            object parameters;

            if (columnsState.HasOwnerUserId)
            {
                selectRunSql = $@"
    SELECT cr.""Id"" AS ""RunId"", cr.""StartedAtUtc"", {runOperatorSql.Projection} AS ""OperatorDisplayName""
    FROM ""CountingRun"" cr
    {InventoryEndpointSupport.AppendJoinClause(runOperatorSql.JoinClause)}
    WHERE cr.""InventorySessionId"" = @SessionId
      AND cr.""LocationId""        = @LocationId
      AND cr.""CountType""         = @CountType
      AND cr.""OwnerUserId""       = @OwnerUserId
      AND cr.""CompletedAtUtc"" IS NULL
      AND EXISTS (SELECT 1 FROM ""CountLine"" cl WHERE cl.""CountingRunId"" = cr.""Id"")
    ORDER BY cr.""StartedAtUtc"" DESC
    LIMIT 1;";

                parameters = new
                {
                    SessionId = targetSessionId,
                    LocationId = locationId,
                    CountType = (short)countType,
                    OwnerUserId = ownerUserId
                };
            }
            else if (columnsState.HasOperatorDisplayName)
            {
                if (string.IsNullOrWhiteSpace(ownerDisplayName))
                {
                    return Results.NotFound(new { message = "Utilisateur introuvable pour déterminer le run actif." });
                }

                selectRunSql = $@"
    SELECT cr.""Id"" AS ""RunId"", cr.""StartedAtUtc"", {runOperatorSql.Projection} AS ""OperatorDisplayName""
    FROM ""CountingRun"" cr
    {InventoryEndpointSupport.AppendJoinClause(runOperatorSql.JoinClause)}
    WHERE cr.""InventorySessionId"" = @SessionId
      AND cr.""LocationId""        = @LocationId
      AND cr.""CountType""         = @CountType
      AND COALESCE(cr.""OperatorDisplayName"", '') = @OperatorDisplayName
      AND cr.""CompletedAtUtc"" IS NULL
      AND EXISTS (SELECT 1 FROM ""CountLine"" cl WHERE cl.""CountingRunId"" = cr.""Id"")
    ORDER BY cr.""StartedAtUtc"" DESC
    LIMIT 1;";

                parameters = new
                {
                    SessionId = targetSessionId,
                    LocationId = locationId,
                    CountType = (short)countType,
                    OperatorDisplayName = ownerDisplayName
                };
            }
            else
            {
                return Results.NotFound(new { message = "Impossible de déterminer l'opérateur pour ce run." });
            }

            var run = await connection.QuerySingleOrDefaultAsync<(Guid RunId, DateTime StartedAtUtc, string? OperatorDisplayName)?>(
                new CommandDefinition(selectRunSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (run is null)
                return Results.NotFound(new { message = "Aucun run actif pour ces critères." });

            return Results.Ok(new
            {
                SessionId = targetSessionId,
                RunId = run.Value.RunId,
                CountType = countType,
                OwnerUserId = ownerUserId,
                OperatorDisplayName = run.Value.OperatorDisplayName ?? ownerDisplayName,
                StartedAtUtc = TimeUtil.ToUtcOffset(run.Value.StartedAtUtc)
            });
        })
        .WithName("GetActiveRunForOperator")
        .WithTags("Inventories")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi(op =>
        {
            op.Summary = "Trouve le run ouvert pour une zone, un type et un utilisateur donné.";
            return op;
        });
    }
}
