using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database.Inventory;

namespace CineBoutique.Inventory.Api.Features.Inventory.Sessions;

internal static class SessionConflictsEndpoints
{
    public static IEndpointRouteBuilder MapSessionConflictsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapGetConflictsEndpoint(app);
        MapGetResolvedEndpoint(app);

        return app;
    }

    private static void MapGetConflictsEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sessions/{sessionId:guid}/conflicts", async (
            Guid sessionId,
            ISessionRepository sessionRepository,
            CancellationToken cancellationToken) =>
        {
            var result = await sessionRepository
                .ResolveConflictsForSessionAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);

            if (!result.SessionExists)
                return Results.NotFound();

            var response = new SessionConflictsResponseDto
            {
                SessionId = result.SessionId,
                Items = result.Conflicts
                    .Select(MapConflictItem)
                    .ToArray()
            };

            return Results.Ok(response);
        })
        .WithName("GetSessionConflicts")
        .WithTags("Sessions")
        .Produces<SessionConflictsResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi(op =>
        {
            op.Summary = "Récupère les références toujours en conflit pour une session.";
            op.Description = "Retourne les observations par référence encore en désaccord, avec variance et détails des runs.";
            return op;
        });
    }

    private static void MapGetResolvedEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sessions/{sessionId:guid}/resolved", async (
            Guid sessionId,
            ISessionRepository sessionRepository,
            CancellationToken cancellationToken) =>
        {
            var result = await sessionRepository
                .ResolveConflictsForSessionAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);

            if (!result.SessionExists)
                return Results.NotFound();

            var response = new SessionResolvedConflictsResponseDto
            {
                SessionId = result.SessionId,
                Items = result.Resolved
                    .Select(MapResolvedItem)
                    .ToArray()
            };

            return Results.Ok(response);
        })
        .WithName("GetSessionResolvedConflicts")
        .WithTags("Sessions")
        .Produces<SessionResolvedConflictsResponseDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithOpenApi(op =>
        {
            op.Summary = "Récupère les références sorties du conflit pour une session.";
            op.Description = "Liste les références résolues par la règle des doubles comptages, avec la quantité retenue.";
            return op;
        });
    }

    private static SessionConflictItemDto MapConflictItem(SessionConflictItem item) =>
        new()
        {
            ProductId = item.ProductId,
            ProductRef = item.ProductRef,
            Sku = item.Sku,
            Name = item.Name,
            SampleVariance = item.SampleVariance,
            ResolvedQuantity = item.ResolvedQuantity,
            Observations = item.Observations
                .Select(MapObservation)
                .ToArray()
        };

    private static SessionConflictObservationDto MapObservation(SessionConflictObservation observation) =>
        new()
        {
            RunId = observation.RunId,
            CountType = observation.CountType,
            Quantity = observation.Quantity,
            CountedBy = observation.CountedByDisplayName,
            CountedAtUtc = observation.CountedAtUtc
        };

    private static SessionResolvedConflictDto MapResolvedItem(SessionResolvedConflictItem item) =>
        new()
        {
            ProductId = item.ProductId,
            ProductRef = item.ProductRef,
            Sku = item.Sku,
            Name = item.Name,
            ResolvedQuantity = item.ResolvedQuantity,
            ResolutionRule = item.ResolutionRule,
            ResolvedAtUtc = item.ResolvedAtUtc
        };
}
