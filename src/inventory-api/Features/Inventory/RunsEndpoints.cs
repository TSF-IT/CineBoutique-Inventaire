using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure;
using CineBoutique.Inventory.Api.Infrastructure.Logging;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database.Inventory;
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
            IRunRepository runRepository,
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

            var summaryModel = await runRepository
                .GetSummaryAsync(parsedShopId, cancellationToken)
                .ConfigureAwait(false);

            var openRunDetails = summaryModel.OpenRuns
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

            var completedRunDetails = summaryModel.CompletedRuns
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

            var conflictZones = summaryModel.ConflictZones
                .Select(row => new ConflictZoneSummaryDto
                {
                    LocationId = row.LocationId,
                    LocationCode = row.LocationCode,
                    LocationLabel = row.LocationLabel,
                    ConflictLines = row.ConflictLines
                })
                .ToList();

            var payload = new InventorySummaryDto
            {
                ActiveSessions = summaryModel.ActiveSessions,
                LastActivityUtc = TimeUtil.ToUtcOffset(summaryModel.LastActivityUtc),
                OpenRunDetails = openRunDetails,
                CompletedRunDetails = completedRunDetails,
                ConflictZones = conflictZones,
                OpenRuns = openRunDetails.Count,
                CompletedRuns = completedRunDetails.Count,
                Conflicts = conflictZones.Count
            };

            var summaryQuery = FormattableString.Invariant($"conflicts summary shop={parsedShopId} zones={payload.Conflicts}");
            ApiLog.InventorySearch(logger, summaryQuery);

            return Results.Ok(payload);
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
            IRunRepository runRepository,
            CancellationToken cancellationToken) =>
        {
            var detail = await runRepository
                .GetCompletedRunDetailAsync(runId, cancellationToken)
                .ConfigureAwait(false);

            if (detail is null)
            {
                return Results.NotFound();
            }

            var payload = new CompletedRunDetailDto
            {
                RunId = detail.RunId,
                LocationId = detail.LocationId,
                LocationCode = detail.LocationCode,
                LocationLabel = detail.LocationLabel,
                CountType = detail.CountType,
                OperatorDisplayName = detail.OperatorDisplayName,
                StartedAtUtc = TimeUtil.ToUtcOffset(detail.StartedAtUtc),
                CompletedAtUtc = TimeUtil.ToUtcOffset(detail.CompletedAtUtc),
                Items = detail.Items
                    .Select(item => new CompletedRunDetailItemDto
                    {
                        ProductId = item.ProductId,
                        Sku = item.Sku,
                        Name = item.Name,
                        Ean = item.Ean,
                        Quantity = item.Quantity
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
            IRunRepository runRepository,
            CancellationToken cancellationToken) =>
        {
            if (countType < 1)
            {
                return Results.BadRequest(new { message = "countType doit être supérieur ou égal à 1." });
            }

            if (ownerUserId == Guid.Empty)
            {
                return Results.BadRequest(new { message = "ownerUserId est requis." });
            }

            var lookup = await runRepository
                .FindActiveRunAsync(locationId, (short)countType, ownerUserId, sessionId, cancellationToken)
                .ConfigureAwait(false);

            return lookup.Status switch
            {
                ActiveRunLookupStatus.Success when lookup.Run is not null => Results.Ok(new
                {
                    SessionId = lookup.SessionId,
                    RunId = lookup.Run.RunId,
                    CountType = countType,
                    OwnerUserId = lookup.OwnerUserId,
                    OperatorDisplayName = lookup.Run.OperatorDisplayName ?? lookup.OwnerDisplayName,
                    StartedAtUtc = TimeUtil.ToUtcOffset(lookup.Run.StartedAtUtc)
                }),
                ActiveRunLookupStatus.NoActiveSession => Results.NotFound(new { message = "Aucune session active." }),
                ActiveRunLookupStatus.OperatorNotSupported => Results.NotFound(new { message = "Impossible de déterminer l'opérateur pour ce run." }),
                ActiveRunLookupStatus.OwnerDisplayNameMissing => Results.NotFound(new { message = "Utilisateur introuvable pour déterminer le run actif." }),
                ActiveRunLookupStatus.RunNotFound => Results.NotFound(new { message = "Aucun run actif pour ces critères." }),
                _ => Results.NotFound(new { message = "Aucun run actif pour ces critères." })
            };
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
