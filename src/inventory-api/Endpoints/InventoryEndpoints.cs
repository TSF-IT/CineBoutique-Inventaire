// Modifications : externalisation des endpoints inventaire avec ajout de la génération de conflits lors des clôtures.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Infrastructure;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Models;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Npgsql;

namespace CineBoutique.Inventory.Api.Endpoints;

// Type marqueur non statique, uniquement pour la catégorisation des logs
internal sealed class InventoryEndpointsMarker { }

internal static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapSummaryEndpoint(app);
        MapCompletedRunDetailEndpoint(app);
        MapLocationsEndpoint(app);
        MapStartEndpoint(app);
        MapCompleteEndpoint(app);
        MapReleaseEndpoint(app);
        MapRestartEndpoint(app);
        MapActiveRunLookupEndpoint(app);
        MapConflictZoneDetailEndpoint(app);

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

            summary.ConflictZones = conflictZoneRows
                .Select(row => new ConflictZoneSummaryDto
                {
                    LocationId = row.LocationId,
                    LocationCode = row.LocationCode,
                    LocationLabel = row.LocationLabel,
                    ConflictLines = row.ConflictLines
                })
                .ToList();

            summary.Conflicts = summary.ConflictZones.Count;

            logger.LogDebug("ConflictsSummary shop={ShopId} zones={Count}", parsedShopId, summary.Conflicts);

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

            var columnsState = await DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
            var runOperatorSql = BuildOperatorSqlFragments("cr", "owner", columnsState);

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
{AppendJoinClause(runOperatorSql.JoinClause)}
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

    private static void MapConflictZoneDetailEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/conflicts/{locationId:guid}", async (
            Guid locationId,
            IDbConnection connection,
            [FromServices] ILogger<InventoryEndpointsMarker> logger,
            CancellationToken cancellationToken) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            const string locationSql =
                "SELECT \"Id\" AS \"Id\", \"Code\" AS \"Code\", \"Label\" AS \"Label\" FROM \"Location\" WHERE \"Id\" = @LocationId";

            var location = await connection.QuerySingleOrDefaultAsync<LocationMetadataRow>(
                new CommandDefinition(locationSql, new { LocationId = locationId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (location is null)
            {
                return Results.NotFound();
            }

            const string lastRunsSql = @"SELECT DISTINCT ON (cr.""CountType"")
    cr.""CountType"" AS ""CountType"",
    cr.""Id""        AS ""RunId""
FROM ""CountingRun"" cr
WHERE cr.""LocationId"" = @LocationId
  AND cr.""CompletedAtUtc"" IS NOT NULL
  AND cr.""CountType"" IN (1, 2)
ORDER BY cr.""CountType"", cr.""CompletedAtUtc"" DESC;";

            var runLookup = (await connection.QueryAsync<LastRunLookupRow>(
                    new CommandDefinition(lastRunsSql, new { LocationId = locationId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            var run1Id = runLookup.FirstOrDefault(row => row.CountType == 1)?.RunId;
            var run2Id = runLookup.FirstOrDefault(row => row.CountType == 2)?.RunId;

            if (run1Id is null || run2Id is null)
            {
                logger.LogDebug(
                    "ConflictsZoneDetail location={LocationId} missing runs count1={HasRun1} count2={HasRun2}",
                    locationId,
                    run1Id is not null,
                    run2Id is not null);

                var emptyPayload = new ConflictZoneDetailDto
                {
                    LocationId = location.Id,
                    LocationCode = location.Code,
                    LocationLabel = location.Label,
                    Items = Array.Empty<ConflictZoneItemDto>()
                };

                return Results.Ok(emptyPayload);
            }

            const string detailSql = @"WITH conflict_products AS (
    SELECT DISTINCT cl.""ProductId""
    FROM ""Conflict"" c
    JOIN ""CountLine"" cl ON cl.""Id"" = c.""CountLineId""
    JOIN ""CountingRun"" cr ON cr.""Id"" = cl.""CountingRunId""
    WHERE c.""ResolvedAtUtc"" IS NULL
      AND cr.""LocationId"" = @LocationId
)
SELECT
    p.""Id""  AS ""ProductId"",
    p.""Ean"" AS ""Ean"",
    COALESCE(SUM(CASE WHEN cl.""CountingRunId"" = @Run1 THEN cl.""Quantity"" END), 0)::int AS ""QtyC1"",
    COALESCE(SUM(CASE WHEN cl.""CountingRunId"" = @Run2 THEN cl.""Quantity"" END), 0)::int AS ""QtyC2""
FROM conflict_products cp
JOIN ""Product"" p ON p.""Id"" = cp.""ProductId""
LEFT JOIN ""CountLine"" cl ON cl.""ProductId"" = cp.""ProductId""
    AND cl.""CountingRunId"" IN (@Run1, @Run2)
GROUP BY p.""Id"", p.""Ean""
HAVING COALESCE(SUM(CASE WHEN cl.""CountingRunId"" = @Run1 THEN cl.""Quantity"" END), 0)
    <> COALESCE(SUM(CASE WHEN cl.""CountingRunId"" = @Run2 THEN cl.""Quantity"" END), 0)
ORDER BY p.""Ean"";";

            var items = (await connection.QueryAsync<ConflictZoneItemRow>(
                    new CommandDefinition(
                        detailSql,
                        new { LocationId = locationId, Run1 = run1Id, Run2 = run2Id },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            var payload = new ConflictZoneDetailDto
            {
                LocationId = location.Id,
                LocationCode = location.Code,
                LocationLabel = location.Label,
                Items = items
                    .Select(row => new ConflictZoneItemDto
                    {
                        ProductId = row.ProductId,
                        Ean = row.Ean,
                        QtyC1 = row.QtyC1,
                        QtyC2 = row.QtyC2
                    })
                    .ToList()
            };

            logger.LogDebug("ConflictsZoneDetail location={LocationId} items={ItemCount}", locationId, payload.Items.Count);

            return Results.Ok(payload);
        })
        .WithName("GetConflictZoneDetail")
        .WithTags("Conflicts")
        .Produces<ConflictZoneDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi(op =>
        {
            op.Summary = "Récupère le détail des divergences pour une zone.";
            op.Description = "Liste les références en conflit entre les deux derniers passages de comptage.";
            return op;
        });
    }

    private static void MapLocationsEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/locations",
            async (string? shopId, int? countType, IDbConnection connection, CancellationToken cancellationToken) =>
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

            if (countType.HasValue && countType.Value is not (1 or 2))
            {
                return Results.BadRequest(new
                {
                    message = "Le paramètre countType doit être 1 (premier passage) ou 2 (second passage)."
                });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var columnsState = await DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
            var locationOperatorSql = BuildOperatorSqlFragments("cr", "owner", columnsState);

            var activeRunsDistinctColumns = columnsState.HasOwnerUserId
                ? "cr.\"LocationId\", cr.\"CountType\", cr.\"OwnerUserId\""
                : columnsState.HasOperatorDisplayName
                    ? "cr.\"LocationId\", cr.\"CountType\", cr.\"OperatorDisplayName\""
                    : "cr.\"LocationId\", cr.\"CountType\"";

            var activeRunsOrderByColumns = columnsState.HasOwnerUserId
                ? "cr.\"LocationId\", cr.\"CountType\", cr.\"OwnerUserId\", cr.\"StartedAtUtc\" DESC"
                : columnsState.HasOperatorDisplayName
                    ? "cr.\"LocationId\", cr.\"CountType\", cr.\"OperatorDisplayName\", cr.\"StartedAtUtc\" DESC"
                    : "cr.\"LocationId\", cr.\"CountType\", cr.\"StartedAtUtc\" DESC";

            var sql = $@"WITH active_runs AS (
    SELECT DISTINCT ON ({activeRunsDistinctColumns})
        cr.""LocationId"",
        cr.""Id""            AS ""ActiveRunId"",
        cr.""CountType""     AS ""ActiveCountType"",
        cr.""StartedAtUtc""  AS ""ActiveStartedAtUtc"",
        -- si tu as la détection conditionnelle de la colonne:
        {locationOperatorSql.Projection} AS ""BusyBy""
    FROM ""CountingRun"" cr
{AppendJoinClause(locationOperatorSql.JoinClause)}
    WHERE cr.""CompletedAtUtc"" IS NULL
      AND (@CountType IS NULL OR cr.""CountType"" = @CountType)
    ORDER BY {activeRunsOrderByColumns}
)
SELECT
    l.""Id"",
    l.""Code"",
    l.""Label"",
    (ar.""ActiveRunId"" IS NOT NULL) AS ""IsBusy"",
    ar.""BusyBy"",
    CASE
        WHEN ar.""ActiveRunId"" IS NULL THEN NULL
        WHEN ar.""ActiveRunId""::text ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' THEN ar.""ActiveRunId""
        ELSE NULL
    END AS ""ActiveRunId"",
    ar.""ActiveCountType"",
    ar.""ActiveStartedAtUtc""
FROM ""Location"" l
LEFT JOIN active_runs ar ON l.""Id"" = ar.""LocationId""
WHERE l.""ShopId"" = @ShopId
ORDER BY l.""Code"" ASC;";

            var sqlParameters = new
            {
                CountType = countType,
                ShopId = parsedShopId,
            };

            var locations = (await connection
                    .QueryAsync<LocationListItemDto>(new CommandDefinition(sql, sqlParameters, cancellationToken: cancellationToken))
                    .ConfigureAwait(false)).ToList();

            if (locations.Count == 0)
            {
                return Results.Ok(Array.Empty<LocationListItemDto>());
            }

            var locationIds = locations.Select(location => location.Id).ToArray();

            var openRunsOperatorSql = BuildOperatorSqlFragments("cr", "owner", columnsState);

            var openRunsSql = $@"SELECT
    cr.""LocationId"",
    cr.""CountType"",
    cr.""Id""          AS ""RunId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc"",
    {openRunsOperatorSql.OwnerDisplayProjection} AS ""OwnerDisplayName"",
    {openRunsOperatorSql.OperatorDisplayProjection} AS ""OperatorDisplayName"",
    {openRunsOperatorSql.OwnerUserIdProjection} AS ""OwnerUserId""
FROM ""CountingRun"" cr
{AppendJoinClause(openRunsOperatorSql.JoinClause)}
WHERE cr.""CompletedAtUtc"" IS NULL
  AND cr.""LocationId"" = ANY(@LocationIds::uuid[])
ORDER BY cr.""LocationId"", cr.""CountType"", cr.""StartedAtUtc"" DESC;";

            var completedRunsOperatorSql = BuildOperatorSqlFragments("cr", "owner", columnsState);

            var completedRunsSql = $@"SELECT DISTINCT ON (cr.""LocationId"", cr.""CountType"")
    cr.""LocationId"",
    cr.""CountType"",
    cr.""Id""           AS ""RunId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc"",
    {completedRunsOperatorSql.OwnerDisplayProjection} AS ""OwnerDisplayName"",
    {completedRunsOperatorSql.OperatorDisplayProjection} AS ""OperatorDisplayName"",
    {completedRunsOperatorSql.OwnerUserIdProjection} AS ""OwnerUserId""
FROM ""CountingRun"" cr
{AppendJoinClause(completedRunsOperatorSql.JoinClause)}
WHERE cr.""CompletedAtUtc"" IS NOT NULL
  AND cr.""LocationId"" = ANY(@LocationIds::uuid[])
ORDER BY cr.""LocationId"", cr.""CountType"", cr.""CompletedAtUtc"" DESC;";

            var openRuns = await connection
                .QueryAsync<LocationCountStatusRow>(new CommandDefinition(openRunsSql, new { LocationIds = locationIds }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var completedRuns = await connection
                .QueryAsync<LocationCountStatusRow>(new CommandDefinition(completedRunsSql, new { LocationIds = locationIds }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var openLookup = openRuns.ToLookup(row => (row.LocationId, row.CountType));
            var completedLookup = completedRuns.ToLookup(row => (row.LocationId, row.CountType));

            static IEnumerable<short> DiscoverCountTypes(IEnumerable<LocationCountStatusRow> runs)
                => runs
                    .Select(row => row.CountType)
                    .Where(countTypeValue => countTypeValue > 0)
                    .Distinct();

            var discoveredCountTypes = DiscoverCountTypes(openRuns).Concat(DiscoverCountTypes(completedRuns));

            var defaultCountTypes = new short[] { 1, 2 };

            if (countType is { } requested)
            {
                defaultCountTypes = defaultCountTypes.Concat(new[] { (short)requested }).ToArray();
            }

            var targetCountTypes = defaultCountTypes
                .Concat(discoveredCountTypes)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

            static string? NormalizeDisplayName(string? value) =>
                string.IsNullOrWhiteSpace(value) ? null : value.Trim();

            static Guid? NormalizeUserId(Guid? value) =>
                value is { } guid && guid != Guid.Empty ? guid : null;

            static DateTimeOffset? ConvertToUtcTimestamp(DateTime? value)
                => value.HasValue ? TimeUtil.ToUtcOffset(value.Value) : (DateTimeOffset?)null;

            foreach (var location in locations)
            {
                var statuses = new List<LocationCountStatusDto>(targetCountTypes.Length);

                foreach (var type in targetCountTypes)
                {
                    var status = new LocationCountStatusDto
                    {
                        CountType = type,
                        Status = LocationCountStatus.NotStarted,
                        RunId = null,
                        OwnerDisplayName = null,
                        OwnerUserId = null,
                        StartedAtUtc = null,
                        CompletedAtUtc = null
                    };

                    var open = openLookup[(location.Id, type)].FirstOrDefault();
                    if (open is not null)
                    {
                        status.Status = LocationCountStatus.InProgress;
                        status.RunId = EndpointUtilities.SanitizeRunId(open.RunId);
                        status.OwnerDisplayName = NormalizeDisplayName(open.OwnerDisplayName);
                        status.OwnerUserId = NormalizeUserId(open.OwnerUserId);
                        status.StartedAtUtc = ConvertToUtcTimestamp(open.StartedAtUtc);
                        status.CompletedAtUtc = ConvertToUtcTimestamp(open.CompletedAtUtc);
                    }
                    else
                    {
                        var completed = completedLookup[(location.Id, type)].FirstOrDefault();
                        if (completed is not null)
                        {
                            status.Status = LocationCountStatus.Completed;
                            status.RunId = EndpointUtilities.SanitizeRunId(completed.RunId);
                            status.OwnerDisplayName = NormalizeDisplayName(completed.OwnerDisplayName);
                            status.OwnerUserId = NormalizeUserId(completed.OwnerUserId);
                            status.StartedAtUtc = ConvertToUtcTimestamp(completed.StartedAtUtc);
                            status.CompletedAtUtc = ConvertToUtcTimestamp(completed.CompletedAtUtc);
                        }
                    }

                    statuses.Add(status);
                }

                location.CountStatuses = statuses.Count > 0
                    ? statuses
                    : Array.Empty<LocationCountStatusDto>();

                var openRunsForLocation = openRuns
                    .Where(r => r.LocationId == location.Id)
                    .ToList();

                // Y a-t-il un run ouvert correspondant au filtre éventuel ?
                if (countType is { } requestedType)
                {
                    var runsForRequestedType = openRunsForLocation
                        .Where(r => r.CountType == requestedType)
                        .ToList();

                    location.IsBusy = runsForRequestedType.Any();

                    var mostRecent = runsForRequestedType
                        .OrderByDescending(r => r.StartedAtUtc)
                        .FirstOrDefault();

                    location.ActiveRunId = EndpointUtilities.SanitizeRunId(mostRecent?.RunId);
                    location.ActiveCountType = mostRecent?.CountType;
                    location.ActiveStartedAtUtc = TimeUtil.ToUtcOffset(mostRecent?.StartedAtUtc);
                    var normalizedBusy = NormalizeDisplayName(mostRecent?.OwnerDisplayName)
                        ?? NormalizeDisplayName(mostRecent?.OperatorDisplayName);
                    location.BusyBy = normalizedBusy;
                }
                else
                {
                    location.IsBusy = openRunsForLocation.Any();

                    var mostRecent = openRunsForLocation
                        .OrderByDescending(r => r.StartedAtUtc)
                        .FirstOrDefault();

                    location.ActiveRunId = EndpointUtilities.SanitizeRunId(mostRecent?.RunId);
                    location.ActiveCountType = mostRecent?.CountType;
                    location.ActiveStartedAtUtc = TimeUtil.ToUtcOffset(mostRecent?.StartedAtUtc);
                    var normalizedBusy = NormalizeDisplayName(mostRecent?.OwnerDisplayName)
                        ?? NormalizeDisplayName(mostRecent?.OperatorDisplayName);
                    location.BusyBy = normalizedBusy;
                }
            }

            return Results.Ok(locations);
        })
        .WithName("GetLocations")
        .WithTags("Locations")
        .Produces<IEnumerable<LocationListItemDto>>(StatusCodes.Status200OK)
        .WithOpenApi(op =>
        {
            op.Summary = "Liste les emplacements (locations)";
            op.Description = "Retourne les métadonnées et l'état d'occupation des locations, filtré par type de comptage optionnel.";
            op.Parameters ??= new List<OpenApiParameter>();
            if (!op.Parameters.Any(parameter => string.Equals(parameter.Name, "shopId", StringComparison.OrdinalIgnoreCase)))
            {
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "shopId",
                    In = ParameterLocation.Query,
                    Required = true,
                    Description = "Identifiant unique de la boutique dont on souhaite récupérer les zones.",
                    Schema = new OpenApiSchema { Type = "string", Format = "uuid" }
                });
            }

            if (!op.Parameters.Any(parameter => string.Equals(parameter.Name, "countType", StringComparison.OrdinalIgnoreCase)))
            {
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "countType",
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = "Type de comptage ciblé (1 pour premier passage, 2 pour second, 3 pour contrôle).",
                    Schema = new OpenApiSchema { Type = "integer", Minimum = 1 }
                });
            }
            return op;
        });
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
                "SELECT \"Id\", \"ShopId\", \"Code\", \"Label\" FROM \"Location\" WHERE \"Id\" = @LocationId AND \"ShopId\" = @ShopId LIMIT 1;";

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

            var columnsState = await DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);

            if (connection is not NpgsqlConnection npgsqlConnection)
            {
                return Results.Problem(
                    "La connexion à la base de données n'est pas compatible avec PostgreSQL.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            if (!await ValidateUserBelongsToShop(npgsqlConnection, request.OwnerUserId, location.ShopId, cancellationToken).ConfigureAwait(false))
            {
                return BadOwnerUser(request.OwnerUserId, location.ShopId);
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

            var activeOperatorSql = BuildOperatorSqlFragments("cr", "owner", columnsState);

            var selectActiveSql = $@"SELECT
    cr.""Id""                AS ""RunId"",
    cr.""InventorySessionId"" AS ""InventorySessionId"",
    cr.""StartedAtUtc""       AS ""StartedAtUtc"",
    {(columnsState.HasOwnerUserId ? "cr.\"OwnerUserId\"" : "NULL::uuid")} AS ""OwnerUserId"",
    {activeOperatorSql.Projection} AS ""OperatorDisplayName""
FROM ""CountingRun"" cr
{AppendJoinClause(activeOperatorSql.JoinClause)}
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

            await using var transaction = await dbConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

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
                var ean = item.Ean?.Trim();
                if (string.IsNullOrWhiteSpace(ean))
                {
                    additionalFailures.Add(new ValidationFailure($"items[{index}].ean", "Chaque ligne doit contenir un EAN."));
                    continue;
                }

                if (ean.Length is < 8 or > 13 || !ean.All(char.IsDigit))
                {
                    additionalFailures.Add(new ValidationFailure($"items[{index}].ean", $"L'EAN {ean} est invalide. Il doit contenir entre 8 et 13 chiffres."));
                    continue;
                }

                if (item.Quantity < 0)
                {
                    additionalFailures.Add(new ValidationFailure($"items[{index}].quantity", $"La quantité pour l'EAN {ean} doit être positive ou nulle."));
                    continue;
                }

                sanitizedItems.Add(new SanitizedCountLine(ean, item.Quantity, item.IsManual));
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

            var columnsState = await DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);

            const string selectLocationSql =
                "SELECT \"Id\", \"ShopId\", \"Code\", \"Label\" FROM \"Location\" WHERE \"Id\" = @LocationId LIMIT 1;";

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

            if (connection is not NpgsqlConnection npgsqlConnection)
            {
                return Results.Problem(
                    "La connexion à la base de données n'est pas compatible avec PostgreSQL.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            if (!await ValidateUserBelongsToShop(npgsqlConnection, request.OwnerUserId, location.ShopId, cancellationToken).ConfigureAwait(false))
            {
                return BadOwnerUser(request.OwnerUserId, location.ShopId);
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

            await using var transaction = await dbConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var runOperatorSql = BuildOperatorSqlFragments("cr", "owner", columnsState);

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
{AppendJoinClause(runOperatorSql.JoinClause)}
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

            var now = DateTimeOffset.UtcNow;

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
                                OwnerUserId = request.OwnerUserId,
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
                OwnerUserId = request.OwnerUserId,
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

            const string selectProductsSql = "SELECT \"Id\", \"Ean\" FROM \"Product\" WHERE \"Ean\" = ANY(@Eans::text[]);";
            var existingProducts = (await connection
                    .QueryAsync<ProductLookupRow>(
                        new CommandDefinition(selectProductsSql, new { Eans = requestedEans }, transaction, cancellationToken: cancellationToken))
                    .ConfigureAwait(false))
                .ToDictionary(row => row.Ean, row => row.Id, StringComparer.Ordinal);

            const string insertProductSql =
                "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc);";

            const string insertLineSql =
                "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@Id, @RunId, @ProductId, @Quantity, @CountedAtUtc);";

            foreach (var item in aggregatedItems)
            {
                if (!existingProducts.TryGetValue(item.Ean, out var productId))
                {
                    productId = Guid.NewGuid();
                    var sku = BuildUnknownSku(item.Ean);
                    var name = $"Produit inconnu EAN {item.Ean}";

                    await connection
                        .ExecuteAsync(
                            new CommandDefinition(
                                insertProductSql,
                                new
                                {
                                    Id = productId,
                                    Sku = sku,
                                    Name = name,
                                    Ean = item.Ean,
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

            await ManageConflictsAsync(connection, transaction, locationId, countingRunId, countType, now, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            var response = new CompleteInventoryRunResponse
            {
                RunId = countingRunId,
                InventorySessionId = inventorySessionId,
                LocationId = locationId,
                CountType = countType,
                CompletedAtUtc = now,
                ItemsCount = aggregatedItems.Count,
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

            var columnsState = await DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
            var runOperatorSql = BuildOperatorSqlFragments("cr", "owner", columnsState);

            var selectRunSql = $@"
SELECT
    cr.""InventorySessionId"" AS ""InventorySessionId"",
    l.""ShopId""              AS ""ShopId"",
    {(columnsState.HasOwnerUserId ? "cr.\"OwnerUserId\"" : "NULL::uuid")} AS ""OwnerUserId"",
    {runOperatorSql.Projection} AS ""OperatorDisplayName""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
{AppendJoinClause(runOperatorSql.JoinClause)}
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
                if (!await ValidateUserBelongsToShop(npgsqlConnection, request.OwnerUserId, run.Value.ShopId, cancellationToken).ConfigureAwait(false))
                {
                    return BadOwnerUser(request.OwnerUserId, run.Value.ShopId);
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

            await using var transaction = await dbConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

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
                "SELECT \"Id\", \"ShopId\", \"Code\", \"Label\" FROM \"Location\" WHERE \"Id\" = @LocationId LIMIT 1;";

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

            if (connection is NpgsqlConnection npgsqlConnection)
            {
                if (!await ValidateUserBelongsToShop(npgsqlConnection, request.OwnerUserId, location.ShopId, cancellationToken).ConfigureAwait(false))
                {
                    return BadOwnerUser(request.OwnerUserId, location.ShopId);
                }
            }

            const string sql = @"UPDATE ""CountingRun""
SET ""CompletedAtUtc"" = @NowUtc
WHERE ""LocationId"" = @LocationId
  AND ""CompletedAtUtc"" IS NULL
  AND ""CountType"" = @CountType;";

            var now = DateTimeOffset.UtcNow;
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

            var columnsState = await DetectOperatorColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
            var runOperatorSql = BuildOperatorSqlFragments("cr", "owner", columnsState);

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
    {AppendJoinClause(runOperatorSql.JoinClause)}
    WHERE cr.""InventorySessionId"" = @SessionId
      AND cr.""LocationId""        = @LocationId
      AND cr.""CountType""         = @CountType
      AND cr.""OwnerUserId""       = @OwnerUserId
      AND cr.""CompletedAtUtc"" IS NULL
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
    {AppendJoinClause(runOperatorSql.JoinClause)}
    WHERE cr.""InventorySessionId"" = @SessionId
      AND cr.""LocationId""        = @LocationId
      AND cr.""CountType""         = @CountType
      AND COALESCE(cr.""OperatorDisplayName"", '') = @OperatorDisplayName
      AND cr.""CompletedAtUtc"" IS NULL
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


    private static string BuildUnknownSku(string ean)
    {
        if (string.IsNullOrWhiteSpace(ean))
        {
            return $"UNK-{Guid.NewGuid():N}"[..32];
        }

        var normalized = ean.Trim();
        if (normalized.Length > 13)
        {
            normalized = normalized[^13..];
        }

        var sku = $"UNK-{normalized}";
        if (sku.Length <= 32)
        {
            return sku;
        }

        return sku[^32..];
    }

    private static async Task ManageInitialConflictsAsync(
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

    private static async Task ManageAdditionalConflictsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid locationId,
        Guid currentRunId,
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

        if (!hasMatch)
        {
            return;
        }

        const string resolveConflictsSql = @"DELETE FROM ""Conflict""
USING ""CountLine"" cl
JOIN ""CountingRun"" cr ON cr.""Id"" = cl.""CountingRunId""
WHERE ""Conflict"".""CountLineId"" = cl.""Id""
  AND cr.""LocationId"" = @LocationId
  AND ""Conflict"".""ResolvedAtUtc"" IS NULL;";

        await connection.ExecuteAsync(
                new CommandDefinition(
                    resolveConflictsSql,
                    new { LocationId = locationId },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private static Dictionary<Guid, Dictionary<string, decimal>> BuildQuantityLookup(IEnumerable<AggregatedCountRow> aggregatedRows)
        => aggregatedRows
            .GroupBy(row => row.CountingRunId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(
                    row => BuildProductKey(row.ProductId, row.Ean),
                    row => row.Quantity,
                    StringComparer.Ordinal),
                EqualityComparer<Guid>.Default);

    private static bool HaveIdenticalQuantities(
        IDictionary<string, decimal> current,
        IDictionary<string, decimal> reference)
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

    private static Guid? ResolveCountLineId<TInner>(
        IDictionary<Guid, TInner> lookup,
        Guid currentRunId,
        Guid counterpartRunId,
        string key)
    where TInner : IDictionary<string, Guid>
    {
        if (lookup.TryGetValue(currentRunId, out var currentLines) && currentLines.TryGetValue(key, out var currentLineId))
            return currentLineId;

        if (lookup.TryGetValue(counterpartRunId, out var counterpartLines) && counterpartLines.TryGetValue(key, out var counterpartLineId))
            return counterpartLineId;

        return null;
    }

    private sealed record OperatorColumnsState(bool HasOperatorDisplayName, bool OperatorDisplayNameIsNullable, bool HasOwnerUserId);

    private sealed record OperatorSqlFragments(
        string Projection,
        string OwnerDisplayProjection,
        string OperatorDisplayProjection,
        string OwnerUserIdProjection,
        string? JoinClause);

    private static async Task<OperatorColumnsState> DetectOperatorColumnsAsync(
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

    private static OperatorSqlFragments BuildOperatorSqlFragments(
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

    private static string AppendJoinClause(string? joinClause) =>
        string.IsNullOrWhiteSpace(joinClause) ? string.Empty : $"\n{joinClause}";

    private static string BuildProductKey(Guid productId, string? ean)
    {
        if (!string.IsNullOrWhiteSpace(ean))
        {
            return ean.Trim();
        }

        return productId.ToString("D");
    }

    private static async Task ManageConflictsAsync(
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

        await ManageAdditionalConflictsAsync(connection, transaction, locationId, currentRunId, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record SanitizedCountLine(string Ean, decimal Quantity, bool IsManual);

    // Déplace idéalement en fichier dédié, sinon laisse-le juste hors de la classe imbriquée.
    public sealed class AggregatedCountRow
    {
        public Guid CountingRunId { get; set; }
        public string Ean { get; set; } = string.Empty;
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }

        // Dapper aime bien ça
        public AggregatedCountRow() { }
    }

    private sealed record CountLineReference(Guid CountingRunId, Guid CountLineId, Guid ProductId, string? Ean);

    private static async Task<bool> ValidateUserBelongsToShop(NpgsqlConnection cn, Guid ownerUserId, Guid shopId, CancellationToken ct)
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

    private static IResult BadOwnerUser(Guid ownerUserId, Guid shopId) =>
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
