using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure;
using CineBoutique.Inventory.Api.Infrastructure.Logging;
using CineBoutique.Inventory.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Api.Features.Inventory;

internal static class ConflictsEndpoints
{
    public static IEndpointRouteBuilder MapConflictsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapConflictZoneDetailEndpoint(app);

        return app;
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
                "SELECT \"Id\" AS \"Id\", \"Code\" AS \"Code\", \"Label\" AS \"Label\", \"Disabled\" FROM \"Location\" WHERE \"Id\" = @LocationId";

            var location = await connection.QuerySingleOrDefaultAsync<LocationMetadataRow>(
                new CommandDefinition(locationSql, new { LocationId = locationId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (location is null)
            {
                return Results.NotFound();
            }

            const string runsSql = """
WITH active_runs AS (
  SELECT DISTINCT cr."Id" AS "RunId", cr."CompletedAtUtc"
  FROM "Conflict" c
  JOIN "CountLine" cl ON cl."Id" = c."CountLineId"
  JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
  WHERE c."ResolvedAtUtc" IS NULL
    AND cr."LocationId" = @LocationId
),
seed_bounds AS (
  SELECT MIN(ar."CompletedAtUtc") AS "MinCompletedAtUtc"
  FROM active_runs ar
),
conflict_runs AS (
  SELECT cr."Id" AS "RunId", cr."CountType", cr."CompletedAtUtc"
  FROM "CountingRun" cr
  CROSS JOIN seed_bounds sb
  WHERE cr."LocationId" = @LocationId
    AND cr."CompletedAtUtc" IS NOT NULL
    AND sb."MinCompletedAtUtc" IS NOT NULL
    AND cr."CompletedAtUtc" >= sb."MinCompletedAtUtc"
)
SELECT cr."RunId", cr."CountType", cr."CompletedAtUtc",
       COALESCE(su."DisplayName", cr2."OperatorDisplayName") AS "OwnerDisplayName"
FROM conflict_runs cr
JOIN "CountingRun" cr2 ON cr2."Id" = cr."RunId"
LEFT JOIN "ShopUser" su ON su."Id" = cr2."OwnerUserId"
ORDER BY cr."CompletedAtUtc" ASC, cr."CountType" ASC;
""";

            var runRows = (await connection.QueryAsync<ConflictRunHeaderRow>(
                    new CommandDefinition(runsSql, new { LocationId = locationId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            if (runRows.Count == 0)
            {
                var emptyPayload = new ConflictZoneDetailDto
                {
                    LocationId = location.Id,
                    LocationCode = location.Code,
                    LocationLabel = location.Label,
                    Runs = Array.Empty<ConflictRunHeaderDto>(),
                    Items = Array.Empty<ConflictZoneItemDto>()
                };

                logger.LogDebug("ConflictsZoneDetail location={LocationId} has no active conflicts", locationId);
                return Results.Ok(emptyPayload);
            }

            const string detailSql = """
WITH active_runs AS (
    SELECT DISTINCT cr."Id" AS "RunId", cr."CompletedAtUtc"
    FROM "Conflict" c
    JOIN "CountLine" cl ON cl."Id" = c."CountLineId"
    JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
    WHERE c."ResolvedAtUtc" IS NULL
      AND cr."LocationId" = @LocationId
),
seed_bounds AS (
    SELECT MIN(ar."CompletedAtUtc") AS "MinCompletedAtUtc"
    FROM active_runs ar
),
conflict_runs AS (
    SELECT cr."Id" AS "RunId", cr."CountType", cr."CompletedAtUtc"
    FROM "CountingRun" cr
    CROSS JOIN seed_bounds sb
    WHERE cr."LocationId" = @LocationId
      AND cr."CompletedAtUtc" IS NOT NULL
      AND sb."MinCompletedAtUtc" IS NOT NULL
      AND cr."CompletedAtUtc" >= sb."MinCompletedAtUtc"
),
conflict_products AS (
    SELECT DISTINCT cl."ProductId" AS "ProductId"
    FROM "Conflict" c
    JOIN "CountLine" cl ON cl."Id" = c."CountLineId"
    JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
    WHERE c."ResolvedAtUtc" IS NULL
      AND cr."LocationId" = @LocationId
),
product_runs AS (
    SELECT
        cp."ProductId",
        cr."RunId"
    FROM conflict_products cp
    CROSS JOIN conflict_runs cr
)
SELECT
    pr."ProductId" AS "ProductId",
    p."Sku"        AS "Sku",
    p."Ean"        AS "Ean",
    p."Name"       AS "Name",
    pr."RunId"     AS "RunId",
    COALESCE(SUM(cl."Quantity"), 0)::int AS "Quantity"
FROM product_runs pr
JOIN "Product" p ON p."Id" = pr."ProductId"
LEFT JOIN "CountLine" cl ON cl."ProductId" = pr."ProductId" AND cl."CountingRunId" = pr."RunId"
GROUP BY pr."ProductId", p."Sku", p."Ean", p."Name", pr."RunId"
ORDER BY COALESCE(NULLIF(p."Sku", ''), p."Ean"), p."Ean", p."Name", pr."RunId";
""";

            var quantityRows = (await connection.QueryAsync<ConflictRunQuantityRow>(
                    new CommandDefinition(detailSql, new { LocationId = locationId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            var runs = runRows
                .Select(row => new ConflictRunHeaderDto
                {
                    RunId = row.RunId,
                    CountType = row.CountType,
                    CompletedAtUtc = DateTime.SpecifyKind(row.CompletedAtUtc, DateTimeKind.Utc),
                    OwnerDisplayName = row.OwnerDisplayName
                })
                .ToList();

            var quantityLookup = quantityRows
                .GroupBy(row => row.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToDictionary(r => r.RunId, r => r.Quantity),
                    EqualityComparer<Guid>.Default);

            var items = quantityLookup
                .Select(pair =>
                {
                    var productId = pair.Key;
                    var runQuantities = pair.Value;
                    var sampleRow = quantityRows.First(row => row.ProductId == productId);
                    var counts = runs
                        .Select(run => new ConflictRunQtyDto
                        {
                            RunId = run.RunId,
                            CountType = run.CountType,
                            Quantity = runQuantities.TryGetValue(run.RunId, out var quantity) ? quantity : 0
                        })
                        .ToList();

                    var qtyC1 = counts.FirstOrDefault(count => count.CountType == 1)?.Quantity ?? 0;
                    var qtyC2 = counts.FirstOrDefault(count => count.CountType == 2)?.Quantity ?? 0;

                    return new ConflictZoneItemDto
                    {
                        ProductId = productId,
                        Sku = sampleRow.Sku ?? string.Empty,
                        Ean = sampleRow.Ean ?? string.Empty,
                        Name = sampleRow.Name ?? string.Empty,
                        QtyC1 = qtyC1,
                        QtyC2 = qtyC2,
                        AllCounts = counts
                    };
                })
                .OrderBy(item => item.Ean, StringComparer.Ordinal)
                .ToList();

            var payload = new ConflictZoneDetailDto
            {
                LocationId = location.Id,
                LocationCode = location.Code,
                LocationLabel = location.Label,
                Runs = runs,
                Items = items
            };

            var detailQuery = FormattableString.Invariant($"conflicts zone detail location={locationId} runs={payload.Runs.Count} items={payload.Items.Count}");
            ApiLog.InventorySearch(logger, detailQuery);

            return Results.Ok(payload);
        })
        .WithName("GetConflictZoneDetail")
        .WithTags("Conflicts")
        .Produces<ConflictZoneDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi(op =>
        {
            op.Summary = "Récupère le détail des divergences pour une zone.";
            op.Description = "Comparatif de tous les comptages en conflit pour la zone.";
            return op;
        });
    }
}
