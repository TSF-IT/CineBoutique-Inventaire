using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database.Inventory;

namespace CineBoutique.Inventory.Api.Features.Inventory.Sessions;

internal sealed class ResetShopInventoryHandler(
    ISessionRepository sessionRepository,
    IAuditLogger auditLogger,
    IClock clock)
{
    private readonly ISessionRepository _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
    private readonly IAuditLogger _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<IResult> HandleAsync(Guid shopId, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (shopId == Guid.Empty)
        {
            return EndpointUtilities.Problem(
                "Requête invalide",
                "shopId doit être un GUID non nul.",
                StatusCodes.Status400BadRequest);
        }

        var resetResult = await _sessionRepository
            .ResetShopInventoryAsync(shopId, cancellationToken)
            .ConfigureAwait(false);

        var actor = EndpointUtilities.FormatActorLabel(httpContext);
        var timestamp = EndpointUtilities.FormatTimestamp(_clock.UtcNow);
        var shopLabel = string.IsNullOrWhiteSpace(resetResult.ShopName)
            ? $"la boutique {shopId:D}"
            : $"la boutique {resetResult.ShopName}";

        var summary =
            $"zones impactées : {resetResult.LocationsAffected}, comptages supprimés : {resetResult.RunsRemoved}, lignes supprimées : {resetResult.CountLinesRemoved}, conflits supprimés : {resetResult.ConflictsRemoved}, sessions fermées : {resetResult.SessionsRemoved}";

        var auditMessage = $"{actor} a réinitialisé {shopLabel} le {timestamp} UTC ({summary}).";
        await _auditLogger.LogAsync(auditMessage, null, "inventories.reset", cancellationToken).ConfigureAwait(false);

        var response = new ResetShopInventoryResponse
        {
            ShopId = resetResult.ShopId,
            ShopName = resetResult.ShopName,
            ZonesCleared = resetResult.LocationsAffected,
            RunsCleared = resetResult.RunsRemoved,
            LinesCleared = resetResult.CountLinesRemoved,
            ConflictsCleared = resetResult.ConflictsRemoved,
            SessionsClosed = resetResult.SessionsRemoved
        };

        return Results.Ok(response);
    }
}
