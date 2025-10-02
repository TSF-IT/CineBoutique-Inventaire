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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging; 

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
        MapAbortEndpoint(app);
        MapRestartEndpoint(app);
        MapActiveRunLookupEndpoint(app);
        MapConflictZoneDetailEndpoint(app);

        return app;
    }

    private static void MapSummaryEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inventories/summary", async (
            IDbConnection connection,
            [FromServices] ILogger<InventoryEndpointsMarker> logger,
            CancellationToken cancellationToken) =>
        {
            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var activitySources = new List<string>
            {
                "SELECT MAX(\"CountedAtUtc\") AS value FROM \"CountLine\"",
                "SELECT MAX(\"StartedAtUtc\") FROM \"CountingRun\"",
                "SELECT MAX(\"CompletedAtUtc\") FROM \"CountingRun\""
            };

            if (await EndpointUtilities.TableExistsAsync(connection, "Audit", cancellationToken).ConfigureAwait(false))
            {
                activitySources.Insert(0, "SELECT MAX(\"CreatedAtUtc\") AS value FROM \"Audit\"");
            }

            var activityUnion = string.Join("\n            UNION ALL\n            ", activitySources);

            var summarySql = $@"SELECT
    (SELECT COUNT(*)::int FROM ""InventorySession"" WHERE ""CompletedAtUtc"" IS NULL) AS ""ActiveSessions"",
    (SELECT COUNT(*)::int FROM ""CountingRun""   WHERE ""CompletedAtUtc"" IS NULL) AS ""OpenRuns"",
    (
        SELECT COUNT(*)::int
        FROM (
            SELECT DISTINCT cr.""LocationId""
            FROM ""Conflict"" c
            JOIN ""CountLine""  cl ON cl.""Id"" = c.""CountLineId""
            JOIN ""CountingRun"" cr ON cr.""Id"" = cl.""CountingRunId""
            WHERE c.""ResolvedAtUtc"" IS NULL
        ) AS conflict_zones
    ) AS ""Conflicts"",
    (
        SELECT MAX(value) FROM (
            {activityUnion}
        ) AS activity
    ) AS ""LastActivityUtc"";";

            var summary = await connection
                .QueryFirstOrDefaultAsync<InventorySummaryDto>(new CommandDefinition(summarySql, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var hasOperatorDisplayNameColumn = await EndpointUtilities.ColumnExistsAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken).ConfigureAwait(false);

            var operatorDisplayNameProjection = hasOperatorDisplayNameColumn
                ? "cr.\"OperatorDisplayName\""
                : "NULL::text";

            var openRunsDetailsSql = $@"SELECT
    cr.""Id""          AS ""RunId"",
    cr.""LocationId"",
    l.""Code""         AS ""LocationCode"",
    l.""Label""        AS ""LocationLabel"",
    cr.""CountType"",
    {operatorDisplayNameProjection} AS ""OperatorDisplayName"",
    cr.""StartedAtUtc""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
WHERE cr.""CompletedAtUtc"" IS NULL
ORDER BY cr.""StartedAtUtc"" DESC;";

            var openRunRows = (await connection
                    .QueryAsync<OpenRunSummaryRow>(new CommandDefinition(openRunsDetailsSql, cancellationToken: cancellationToken))
                    .ConfigureAwait(false)).ToList();

            summary.OpenRunDetails = openRunRows
                .Select(row => new OpenRunSummaryDto
                {
                    RunId = row.RunId,
                    LocationId = row.LocationId,
                    LocationCode = row.LocationCode,
                    LocationLabel = row.LocationLabel,
                    CountType = row.CountType,
                    OperatorDisplayName = row.OperatorDisplayName,
                    StartedAtUtc = TimeUtil.ToUtcOffset(row.StartedAtUtc)
                })
                .ToList();

            var completedRunsSql = $@"SELECT
    cr.""Id""          AS ""RunId"",
    cr.""LocationId"",
    l.""Code""         AS ""LocationCode"",
    l.""Label""        AS ""LocationLabel"",
    cr.""CountType"",
    {operatorDisplayNameProjection} AS ""OperatorDisplayName"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
WHERE cr.""CompletedAtUtc"" IS NOT NULL
ORDER BY cr.""CompletedAtUtc"" DESC
LIMIT 20;";

            var completedRunRows = (await connection
                    .QueryAsync<CompletedRunSummaryRow>(new CommandDefinition(completedRunsSql, cancellationToken: cancellationToken))
                    .ConfigureAwait(false)).ToList();

            summary.CompletedRunDetails = completedRunRows
                .Select(row => new CompletedRunSummaryDto
                {
                    RunId = row.RunId,
                    LocationId = row.LocationId,
                    LocationCode = row.LocationCode,
                    LocationLabel = row.LocationLabel,
                    CountType = row.CountType,
                    OperatorDisplayName = row.OperatorDisplayName,
                    StartedAtUtc = TimeUtil.ToUtcOffset(row.StartedAtUtc),
                    CompletedAtUtc = TimeUtil.ToUtcOffset(row.CompletedAtUtc)
                })
                .ToList();

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
GROUP BY cr.""LocationId"", l.""Code"", l.""Label""
ORDER BY l.""Code"";",
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

            logger.LogDebug("ConflictsSummary zones={Count}", summary.Conflicts);

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

            var hasOperatorDisplayNameColumn = await EndpointUtilities
                .ColumnExistsAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken)
                .ConfigureAwait(false);

            var operatorDisplayNameProjection = hasOperatorDisplayNameColumn
                ? "cr.\"OperatorDisplayName\""
                : "NULL::text";

            var runSql = $@"SELECT
    cr.""Id""           AS ""RunId"",
    cr.""LocationId"",
    l.""Code""          AS ""LocationCode"",
    l.""Label""         AS ""LocationLabel"",
    cr.""CountType"",
    {operatorDisplayNameProjection} AS ""OperatorDisplayName"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc""
FROM ""CountingRun"" cr
JOIN ""Location"" l ON l.""Id"" = cr.""LocationId""
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
        app.MapGet("/api/locations", async (int? countType, IDbConnection connection, CancellationToken cancellationToken) =>
        {
            if (countType.HasValue && countType is not (1 or 2 or 3))
            {
                return Results.BadRequest(new { message = "Le paramètre countType doit valoir 1, 2 ou 3." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var hasOperatorDisplayNameColumn = await EndpointUtilities.ColumnExistsAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken).ConfigureAwait(false);

            var operatorDisplayNameProjection = hasOperatorDisplayNameColumn
                ? "cr.\"OperatorDisplayName\""
                : "NULL::text";

            var activeRunsDistinctColumns = hasOperatorDisplayNameColumn
                ? "cr.\"LocationId\", cr.\"CountType\", cr.\"OperatorDisplayName\""
                : "cr.\"LocationId\", cr.\"CountType\"";

            var activeRunsOrderByColumns = hasOperatorDisplayNameColumn
                ? "cr.\"LocationId\", cr.\"CountType\", cr.\"OperatorDisplayName\", cr.\"StartedAtUtc\" DESC"
                : "cr.\"LocationId\", cr.\"CountType\", cr.\"StartedAtUtc\" DESC";

            var sql = $@"WITH active_runs AS (
    SELECT DISTINCT ON ({activeRunsDistinctColumns})
        cr.""LocationId"",
        cr.""Id""            AS ""ActiveRunId"",
        cr.""CountType""     AS ""ActiveCountType"",
        cr.""StartedAtUtc""  AS ""ActiveStartedAtUtc"",
        -- si tu as la détection conditionnelle de la colonne:
        {operatorDisplayNameProjection} AS ""BusyBy""
    FROM ""CountingRun"" cr
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
ORDER BY l.""Code"" ASC;";

            var locations = (await connection
                    .QueryAsync<LocationListItemDto>(new CommandDefinition(sql, new { CountType = countType }, cancellationToken: cancellationToken))
                    .ConfigureAwait(false)).ToList();

            if (locations.Count == 0)
            {
                return Results.Ok(locations);
            }

            var locationIds = locations.Select(location => location.Id).ToArray();

            var openRunsSql = $@"SELECT
    cr.""LocationId"",
    cr.""CountType"",
    cr.""Id""          AS ""RunId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc"",
    {operatorDisplayNameProjection} AS ""OperatorDisplayName""
FROM ""CountingRun"" cr
WHERE cr.""CompletedAtUtc"" IS NULL
  AND cr.""LocationId"" = ANY(@LocationIds::uuid[])
ORDER BY cr.""LocationId"", cr.""CountType"", cr.""StartedAtUtc"" DESC;";

            var completedRunsSql = $@"SELECT DISTINCT ON (cr.""LocationId"", cr.""CountType"")
    cr.""LocationId"",
    cr.""CountType"",
    cr.""Id""           AS ""RunId"",
    cr.""StartedAtUtc"",
    cr.""CompletedAtUtc"",
    {operatorDisplayNameProjection} AS ""OperatorDisplayName""
FROM ""CountingRun"" cr
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

            foreach (var location in locations)
            {
                var statuses = new List<LocationCountStatusDto>(targetCountTypes.Length);

                foreach (var type in targetCountTypes)
                {
                    var status = new LocationCountStatusDto
                    {
                        CountType = type
                    };

                    var open = openLookup[(location.Id, type)].FirstOrDefault();
                    if (open is not null)
                    {
                        status.Status = LocationCountStatus.InProgress;
                        status.RunId = EndpointUtilities.SanitizeRunId(open.RunId);
                        status.OperatorDisplayName = open.OperatorDisplayName;
                        status.StartedAtUtc = TimeUtil.ToUtcOffset(open.StartedAtUtc);
                    }
                    else
                    {
                        var completed = completedLookup[(location.Id, type)].FirstOrDefault();
                        if (completed is not null)
                        {
                            status.Status = LocationCountStatus.Completed;
                            status.RunId = EndpointUtilities.SanitizeRunId(completed.RunId);
                            status.OperatorDisplayName = completed.OperatorDisplayName;
                            status.StartedAtUtc = TimeUtil.ToUtcOffset(completed.StartedAtUtc);
                            status.CompletedAtUtc = TimeUtil.ToUtcOffset(completed.CompletedAtUtc);
                        }
                    }

                    statuses.Add(status);
                }

                location.CountStatuses = statuses;

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
                    location.BusyBy = mostRecent?.OperatorDisplayName;
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
                    location.BusyBy = mostRecent?.OperatorDisplayName;
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
            if (!op.Parameters.Any(parameter => string.Equals(parameter.Name, "countType", StringComparison.OrdinalIgnoreCase)))
            {
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "countType",
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = "Type de comptage ciblé (1 pour premier passage, 2 pour second, 3 pour contrôle).",
                    Schema = new OpenApiSchema { Type = "integer", Minimum = 1, Maximum = 3 }
                });
            }
            return op;
        });
    }

    private static void MapStartEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/inventories/{locationId:guid}/start", async (
            Guid locationId,
            StartInventoryRunRequest request,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { message = "Le corps de la requête est requis." });
            }

            if (request.CountType is not (1 or 2 or 3))
            {
                return Results.BadRequest(new { message = "Le type de comptage doit valoir 1, 2 ou 3." });
            }

            var operatorName = request.Operator?.Trim();
            if (string.IsNullOrWhiteSpace(operatorName))
            {
                return Results.BadRequest(new { message = "L'opérateur réalisant le comptage est requis." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var hasOperatorDisplayNameColumn = await EndpointUtilities
                .ColumnExistsAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken)
                .ConfigureAwait(false);

            const string selectLocationSql =
                "SELECT \"Id\", \"Code\", \"Label\" FROM \"Location\" WHERE \"Id\" = @LocationId LIMIT 1;";

            var location = await connection
                .QuerySingleOrDefaultAsync<LocationMetadataRow>(
                    new CommandDefinition(selectLocationSql, new { LocationId = locationId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (location is null)
            {
                return Results.NotFound(new { message = "La zone demandée est introuvable." });
            }

            var selectActiveSql = hasOperatorDisplayNameColumn
                ? @"SELECT
    ""Id""                AS ""RunId"",
    ""InventorySessionId"" AS ""InventorySessionId"",
    ""StartedAtUtc""       AS ""StartedAtUtc"",
    ""OperatorDisplayName"" AS ""OperatorDisplayName""
FROM ""CountingRun""
WHERE ""LocationId"" = @LocationId
  AND ""CountType"" = @CountType
  AND ""CompletedAtUtc"" IS NULL
ORDER BY ""StartedAtUtc"" DESC
LIMIT 1;"
                : @"SELECT
    ""Id""                AS ""RunId"",
    ""InventorySessionId"" AS ""InventorySessionId"",
    ""StartedAtUtc""       AS ""StartedAtUtc"",
    NULL::text              AS ""OperatorDisplayName""
FROM ""CountingRun""
WHERE ""LocationId"" = @LocationId
  AND ""CountType"" = @CountType
  AND ""CompletedAtUtc"" IS NULL
ORDER BY ""StartedAtUtc"" DESC
LIMIT 1;";

            var existingRun = await connection
                .QuerySingleOrDefaultAsync<(Guid RunId, Guid InventorySessionId, DateTime StartedAtUtc, string? OperatorDisplayName)?>(
                    new CommandDefinition(
                        selectActiveSql,
                        new { LocationId = locationId, CountType = request.CountType },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (existingRun is { } active)
            {
                if (hasOperatorDisplayNameColumn)
                {
                    var existingOperator = active.OperatorDisplayName?.Trim();
                    if (!string.IsNullOrWhiteSpace(existingOperator) &&
                        !string.Equals(existingOperator, operatorName, StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.Conflict(new
                        {
                            message = $"Comptage déjà en cours par {existingOperator}."
                        });
                    }
                }

                return Results.Ok(new StartInventoryRunResponse
                {
                    RunId = active.RunId,
                    InventorySessionId = active.InventorySessionId,
                    LocationId = locationId,
                    CountType = request.CountType,
                    OperatorDisplayName = active.OperatorDisplayName,
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

            var insertRunSql = hasOperatorDisplayNameColumn
                ? @"INSERT INTO ""CountingRun"" (""Id"", ""InventorySessionId"", ""LocationId"", ""CountType"", ""StartedAtUtc"", ""OperatorDisplayName"")
VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAtUtc, @Operator);"
                : @"INSERT INTO ""CountingRun"" (""Id"", ""InventorySessionId"", ""LocationId"", ""CountType"", ""StartedAtUtc"")
VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAtUtc);";

            var insertParameters = hasOperatorDisplayNameColumn
                ? new
                {
                    Id = runId,
                    SessionId = sessionId,
                    LocationId = locationId,
                    CountType = request.CountType,
                    StartedAtUtc = now,
                    Operator = operatorName
                }
                : new
                {
                    Id = runId,
                    SessionId = sessionId,
                    LocationId = locationId,
                    CountType = request.CountType,
                    StartedAtUtc = now
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
                OperatorDisplayName = hasOperatorDisplayNameColumn ? operatorName : null,
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
            CompleteInventoryRunRequest request,
            IDbConnection connection,
            IAuditLogger auditLogger,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { message = "Le corps de la requête est requis." });
            }

            var countType = request.CountType;
            if (countType is not (1 or 2 or 3))
            {
                return Results.BadRequest(new { message = "Le type de comptage doit valoir 1, 2 ou 3." });
            }

            var operatorName = request.Operator?.Trim();
            if (string.IsNullOrWhiteSpace(operatorName))
            {
                return Results.BadRequest(new { message = "L'opérateur ayant réalisé le comptage est requis." });
            }

            var rawItems = request.Items ?? new List<CompleteInventoryRunItemRequest>();
            if (rawItems.Count == 0)
            {
                return Results.BadRequest(new { message = "Au moins une ligne de comptage doit être fournie." });
            }

            var sanitizedItems = new List<SanitizedCountLine>(rawItems.Count);
            foreach (var item in rawItems)
            {
                var ean = item.Ean?.Trim();
                if (string.IsNullOrWhiteSpace(ean))
                {
                    return Results.BadRequest(new { message = "Chaque ligne doit contenir un EAN." });
                }

                if (ean.Length is < 8 or > 13 || !ean.All(char.IsDigit))
                {
                    return Results.BadRequest(new { message = $"L'EAN {ean} est invalide. Il doit contenir entre 8 et 13 chiffres." });
                }

                if (item.Quantity <= 0)
                {
                    return Results.BadRequest(new { message = $"La quantité pour l'EAN {ean} doit être strictement positive." });
                }

                sanitizedItems.Add(new SanitizedCountLine(ean, item.Quantity, item.IsManual));
            }

            var aggregatedItems = sanitizedItems
                .GroupBy(line => line.Ean, StringComparer.Ordinal)
                .Select(group => new SanitizedCountLine(group.Key, group.Sum(line => line.Quantity), group.Any(line => line.IsManual)))
                .ToList();

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var hasOperatorDisplayNameColumn = await EndpointUtilities.ColumnExistsAsync(
                    connection,
                    "CountingRun",
                    "OperatorDisplayName",
                    cancellationToken).ConfigureAwait(false);

            if (connection is not DbConnection dbConnection)
            {
                return Results.Problem(
                    "La connexion à la base de données n'est pas compatible avec les transactions.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            await using var transaction = await dbConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            const string selectLocationSql =
                "SELECT \"Id\", \"Code\", \"Label\" FROM \"Location\" WHERE \"Id\" = @LocationId LIMIT 1;";

            var location = await connection
                .QuerySingleOrDefaultAsync<LocationMetadataRow>(
                    new CommandDefinition(selectLocationSql, new { LocationId = locationId }, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (location is null)
            {
                return Results.NotFound(new { message = "La zone demandée est introuvable." });
            }

            const string selectRunSql =
                "SELECT \"Id\", \"InventorySessionId\", \"LocationId\", \"CountType\" FROM \"CountingRun\" WHERE \"Id\" = @RunId LIMIT 1;";

            CountingRunRow? existingRun = null;
            if (request.RunId is { } runId)
            {
                existingRun = await connection
                    .QuerySingleOrDefaultAsync<CountingRunRow>(
                        new CommandDefinition(selectRunSql, new { RunId = runId }, transaction, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                if (existingRun is null)
                {
                    return Results.NotFound(new { message = "Le run fourni est introuvable." });
                }

                if (existingRun.LocationId != locationId)
                {
                    return Results.BadRequest(new { message = "Le run ne correspond pas à la zone demandée." });
                }
            }

            if (countType == 2 && hasOperatorDisplayNameColumn)
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
                    string.Equals(firstRunOperator.Trim(), operatorName, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return Results.Conflict(new
                    {
                        message = "Le deuxième comptage doit être réalisé par un opérateur différent du premier."
                    });
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

                const string insertRunSql =
                    "INSERT INTO \"CountingRun\" (\"Id\", \"InventorySessionId\", \"LocationId\", \"CountType\", \"StartedAtUtc\", \"CompletedAtUtc\"{0}) VALUES (@Id, @SessionId, @LocationId, @CountType, @StartedAtUtc, @CompletedAtUtc{1});";

                object insertRunParameters = hasOperatorDisplayNameColumn
                    ? new
                    {
                        Id = countingRunId,
                        SessionId = inventorySessionId,
                        LocationId = locationId,
                        CountType = countType,
                        StartedAtUtc = now,
                        CompletedAtUtc = now,
                        Operator = operatorName
                    }
                    : new
                    {
                        Id = countingRunId,
                        SessionId = inventorySessionId,
                        LocationId = locationId,
                        CountType = countType,
                        StartedAtUtc = now,
                        CompletedAtUtc = now
                    };

                var operatorColumns = hasOperatorDisplayNameColumn ? ", \"OperatorDisplayName\"" : string.Empty;
                var operatorValues = hasOperatorDisplayNameColumn ? ", @Operator" : string.Empty;

                await connection
                    .ExecuteAsync(
                        new CommandDefinition(
                            string.Format(insertRunSql, operatorColumns, operatorValues),
                            insertRunParameters,
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
            }

            const string updateSessionSql =
                "UPDATE \"InventorySession\" SET \"CompletedAtUtc\" = @CompletedAtUtc WHERE \"Id\" = @SessionId;";

            await connection
                .ExecuteAsync(new CommandDefinition(updateSessionSql, new { SessionId = inventorySessionId, CompletedAtUtc = now }, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var updateRunSql = hasOperatorDisplayNameColumn
                ? "UPDATE \"CountingRun\" SET \"CountType\" = @CountType, \"CompletedAtUtc\" = @CompletedAtUtc, \"OperatorDisplayName\" = @Operator WHERE \"Id\" = @RunId;"
                : "UPDATE \"CountingRun\" SET \"CountType\" = @CountType, \"CompletedAtUtc\" = @CompletedAtUtc WHERE \"Id\" = @RunId;";

            object updateRunParameters = hasOperatorDisplayNameColumn
                ? new
                {
                    RunId = countingRunId,
                    CountType = countType,
                    CompletedAtUtc = now,
                    Operator = operatorName
                }
                : new
                {
                    RunId = countingRunId,
                    CountType = countType,
                    CompletedAtUtc = now
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

            var actor = EndpointUtilities.FormatActorLabel(operatorName);
            var timestamp = EndpointUtilities.FormatTimestamp(now);
            var zoneDescription = string.IsNullOrWhiteSpace(location.Code)
                ? location.Label
                : $"{location.Code} – {location.Label}";
            var countDescription = EndpointUtilities.DescribeCountType(countType);
            var auditMessage =
                $"{actor} a terminé {zoneDescription} pour un {countDescription} le {timestamp} UTC ({response.ItemsCount} références, total {response.TotalQuantity}).";

            await auditLogger.LogAsync(auditMessage, operatorName, "inventories.complete.success", cancellationToken).ConfigureAwait(false);

            return Results.Ok(response);
        })
        .WithName("CompleteInventoryRun")
        .WithTags("Inventories")
        .Produces<CompleteInventoryRunResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapAbortEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/inventories/{locationId:guid}/runs/{runId:guid}", async (
            Guid locationId,
            Guid runId,
            string operatorName,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            operatorName = operatorName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(operatorName))
            {
                return Results.BadRequest(new { message = "operatorName est requis." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            var hasOperatorDisplayNameColumn = await EndpointUtilities
                .ColumnExistsAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken)
                .ConfigureAwait(false);

            var selectRunSql = hasOperatorDisplayNameColumn
                ? @"SELECT
    ""InventorySessionId"" AS ""InventorySessionId"",
    ""OperatorDisplayName"" AS ""OperatorDisplayName""
FROM ""CountingRun""
WHERE ""Id"" = @RunId
  AND ""LocationId"" = @LocationId
  AND ""CompletedAtUtc"" IS NULL
LIMIT 1;"
                : @"SELECT
    ""InventorySessionId"" AS ""InventorySessionId"",
    NULL::text              AS ""OperatorDisplayName""
FROM ""CountingRun""
WHERE ""Id"" = @RunId
  AND ""LocationId"" = @LocationId
  AND ""CompletedAtUtc"" IS NULL
LIMIT 1;";

            var run = await connection
                .QuerySingleOrDefaultAsync<(Guid InventorySessionId, string? OperatorDisplayName)?>(
                    new CommandDefinition(
                        selectRunSql,
                        new { RunId = runId, LocationId = locationId },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (run is null)
            {
                return Results.NotFound(new { message = "Aucun comptage actif pour les critères fournis." });
            }

            if (hasOperatorDisplayNameColumn)
            {
                var existingOperator = run.Value.OperatorDisplayName?.Trim();
                if (!string.IsNullOrWhiteSpace(existingOperator) &&
                    !string.Equals(existingOperator, operatorName, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Conflict(new
                    {
                        message = $"Comptage détenu par {existingOperator}."
                    });
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
                    new CommandDefinition(countLinesSql, new { RunId = runId }, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (lineCount > 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Results.Conflict(new
                {
                    message = "Impossible de libérer un comptage contenant des lignes enregistrées."
                });
            }

            const string deleteRunSql = "DELETE FROM \"CountingRun\" WHERE \"Id\" = @RunId;";
            await connection
                .ExecuteAsync(
                    new CommandDefinition(deleteRunSql, new { RunId = runId }, transaction, cancellationToken: cancellationToken))
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
        })
        .WithName("AbortInventoryRun")
        .WithTags("Inventories")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .WithOpenApi(op =>
        {
            op.Summary = "Libère un comptage en cours sans le finaliser.";
            op.Description = "Supprime le run actif lorsqu'aucune ligne n'a été enregistrée, ce qui libère la zone.";
            op.Parameters ??= new List<OpenApiParameter>();
            if (!op.Parameters.Any(parameter => string.Equals(parameter.Name, "operatorName", StringComparison.Ordinal)))
            {
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = "operatorName",
                    In = ParameterLocation.Query,
                    Description = "Nom de l'opérateur qui libère le comptage.",
                    Required = true,
                    Schema = new OpenApiSchema { Type = "string" }
                });
            }
            return op;
        });
    }

    private static void MapRestartEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/inventories/{locationId:guid}/restart", async (
            Guid locationId,
            int countType,
            IDbConnection connection,
            IAuditLogger auditLogger,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (countType is not (1 or 2 or 3))
            {
                return Results.BadRequest(new { message = "Le paramètre countType doit valoir 1, 2 ou 3." });
            }

            await EndpointUtilities.EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

            const string sql = @"UPDATE ""CountingRun""
SET ""CompletedAtUtc"" = @NowUtc
WHERE ""LocationId"" = @LocationId
  AND ""CompletedAtUtc"" IS NULL
  AND ""CountType"" = @CountType;";

            var now = DateTimeOffset.UtcNow;
            var affected = await connection
                .ExecuteAsync(new CommandDefinition(sql, new { LocationId = locationId, CountType = countType, NowUtc = now }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var locationInfo = await connection
                .QuerySingleOrDefaultAsync<LocationMetadataRow>(
                    new CommandDefinition(
                        "SELECT \"Id\", \"Code\", \"Label\" FROM \"Location\" WHERE \"Id\" = @LocationId LIMIT 1",
                        new { LocationId = locationId },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
            var actor = EndpointUtilities.FormatActorLabel(userName);
            var timestamp = EndpointUtilities.FormatTimestamp(now);
            var zoneDescription = locationInfo is not null
                ? $"la zone {locationInfo.Code} – {locationInfo.Label}"
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
            string operatorName,
            Guid? sessionId,
            IDbConnection connection,
            CancellationToken cancellationToken) =>
        {
            if (countType is not (1 or 2 or 3))
                return Results.BadRequest(new { message = "countType doit valoir 1, 2 ou 3." });

            operatorName = operatorName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(operatorName))
                return Results.BadRequest(new { message = "operatorName est requis." });

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

            const string selectRunSql = @"
    SELECT ""Id"" AS ""RunId"", ""StartedAtUtc""
    FROM ""CountingRun""
    WHERE ""InventorySessionId"" = @SessionId
    AND ""LocationId""        = @LocationId
    AND ""CountType""         = @CountType
    AND COALESCE(""OperatorDisplayName"", '') = @Operator
    AND ""CompletedAtUtc"" IS NULL
    ORDER BY ""StartedAtUtc"" DESC
    LIMIT 1;";

            var run = await connection.QuerySingleOrDefaultAsync<(Guid RunId, DateTime StartedAtUtc)?>(
                new CommandDefinition(
                    selectRunSql,
                    new { SessionId = targetSessionId, LocationId = locationId, CountType = (short)countType, Operator = operatorName },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (run is null)
                return Results.NotFound(new { message = "Aucun run actif pour ces critères." });

            return Results.Ok(new
            {
                SessionId = targetSessionId,
                RunId = run.Value.RunId,
                CountType = countType,
                Operator = operatorName,
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
            op.Summary = "Trouve le run ouvert pour une zone/type/opérateur (dans la session active par défaut).";
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

    private static async Task ManageConflictsAsync(
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

        var quantityByRun = aggregatedRows
            .GroupBy(row => row.CountingRunId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(
                    row => BuildProductKey(row.ProductId, row.Ean),
                    row => row.Quantity,
                    StringComparer.Ordinal),
                EqualityComparer<Guid>.Default);

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

    private static string BuildProductKey(Guid productId, string? ean)
    {
        if (!string.IsNullOrWhiteSpace(ean))
        {
            return ean.Trim();
        }

        return productId.ToString("D");
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
}
