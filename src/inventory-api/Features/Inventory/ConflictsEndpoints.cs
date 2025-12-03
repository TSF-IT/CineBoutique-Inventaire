using System.Data;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Logging;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database.Inventory;
using Dapper;
using Microsoft.AspNetCore.Mvc;

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
            [FromServices] ISessionRepository sessionRepository,
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
                return Results.NotFound();

            await sessionRepository.ResolveConflictsForLocationAsync(locationId, cancellationToken).ConfigureAwait(false);

            const string runsSql = """
WITH conflict_products AS (
  SELECT DISTINCT p."Id" AS "ProductId"
  FROM "Conflict" c
  JOIN "CountLine" cl ON cl."Id" = c."CountLineId"
  JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
  JOIN "Product" p ON p."Id" = cl."ProductId"
  WHERE c."IsResolved" = FALSE
    AND cr."LocationId" = @LocationId
),
completed_runs AS (
  SELECT DISTINCT cr."Id" AS "RunId",
                  cr."CountType" AS "CountType",
                  cr."CompletedAtUtc"
  FROM "CountingRun" cr
  WHERE cr."LocationId" = @LocationId
    AND cr."CompletedAtUtc" IS NOT NULL
),
candidate_runs AS (
  SELECT DISTINCT cr."Id"        AS "RunId",
                  cr."CountType" AS "CountType",
                  cr."CompletedAtUtc"
  FROM "CountingRun" cr
  JOIN "CountLine" cl ON cl."CountingRunId" = cr."Id"
  WHERE cr."LocationId" = @LocationId
    AND cr."CompletedAtUtc" IS NOT NULL
    AND cl."ProductId" IN (SELECT "ProductId" FROM conflict_products)
),
max_count_type AS (
  SELECT COALESCE(MAX("CountType"), 0) AS "MaxCountType"
  FROM completed_runs
),
series AS (
  SELECT generate_series(1, m."MaxCountType") AS "CountType"
  FROM max_count_type m
  WHERE m."MaxCountType" > 0
),
series_runs AS (
  SELECT s."CountType",
         cr."Id"        AS "RunId",
         cr."CompletedAtUtc"
  FROM series s
  CROSS JOIN LATERAL (
    SELECT crs."RunId",
           crs."CompletedAtUtc"
    FROM completed_runs crs
    WHERE crs."CountType" = s."CountType"
    ORDER BY crs."CompletedAtUtc" DESC, crs."RunId" DESC
    LIMIT 1
  ) cr
),
runs_in_scope AS (
  SELECT DISTINCT "RunId", "CountType", "CompletedAtUtc"
  FROM (
    SELECT "RunId", "CountType", "CompletedAtUtc" FROM candidate_runs
    UNION ALL
    SELECT "RunId", "CountType", "CompletedAtUtc" FROM series_runs
  ) scoped
)
SELECT rs."RunId",
       rs."CountType",
       rs."CompletedAtUtc",
       COALESCE(su."DisplayName", cr."OperatorDisplayName") AS "OwnerDisplayName"
FROM runs_in_scope rs
JOIN "CountingRun" cr ON cr."Id" = rs."RunId"
LEFT JOIN "ShopUser" su ON su."Id" = cr."OwnerUserId"
ORDER BY rs."CompletedAtUtc" ASC, rs."CountType" ASC;
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
WITH conflict_products AS (
    SELECT DISTINCT p."Id" AS "ProductId"
    FROM "Conflict" c
    JOIN "CountLine" cl ON cl."Id" = c."CountLineId"
    JOIN "CountingRun" cr ON cr."Id" = cl."CountingRunId"
    JOIN "Product" p ON p."Id" = cl."ProductId"
    WHERE c."IsResolved" = FALSE
      AND cr."LocationId" = @LocationId
),
completed_runs AS (
    SELECT DISTINCT cr."Id" AS "RunId",
                    cr."CountType" AS "CountType",
                    cr."CompletedAtUtc"
    FROM "CountingRun" cr
    WHERE cr."LocationId" = @LocationId
      AND cr."CompletedAtUtc" IS NOT NULL
),
candidate_runs AS (
    SELECT DISTINCT cr."Id"        AS "RunId",
                    cr."CountType" AS "CountType",
                    cr."CompletedAtUtc"
    FROM "CountingRun" cr
    JOIN "CountLine" cl ON cl."CountingRunId" = cr."Id"
    WHERE cr."LocationId" = @LocationId
      AND cr."CompletedAtUtc" IS NOT NULL
      AND cl."ProductId" IN (SELECT "ProductId" FROM conflict_products)
),
max_count_type AS (
    SELECT COALESCE(MAX("CountType"), 0) AS "MaxCountType"
    FROM completed_runs
),
series AS (
    SELECT generate_series(1, m."MaxCountType") AS "CountType"
    FROM max_count_type m
    WHERE m."MaxCountType" > 0
),
series_runs AS (
    SELECT s."CountType",
           cr."Id"        AS "RunId",
           cr."CompletedAtUtc"
    FROM series s
    CROSS JOIN LATERAL (
        SELECT crs."RunId",
               crs."CompletedAtUtc"
        FROM completed_runs crs
        WHERE crs."CountType" = s."CountType"
        ORDER BY crs."CompletedAtUtc" DESC, crs."RunId" DESC
        LIMIT 1
    ) cr
),
runs_in_scope AS (
    SELECT DISTINCT "RunId", "CountType", "CompletedAtUtc"
    FROM (
        SELECT "RunId", "CountType", "CompletedAtUtc" FROM candidate_runs
        UNION ALL
        SELECT "RunId", "CountType", "CompletedAtUtc" FROM series_runs
    ) scoped
),
product_runs AS (
    SELECT
        cp."ProductId",
        rs."RunId"
    FROM conflict_products cp
    CROSS JOIN runs_in_scope rs
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
