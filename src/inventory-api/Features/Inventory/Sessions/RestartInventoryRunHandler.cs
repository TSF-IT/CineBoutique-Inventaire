using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database.Inventory;
using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace CineBoutique.Inventory.Api.Features.Inventory.Sessions;

internal sealed class RestartInventoryRunHandler
{
    private readonly IValidator<RestartRunRequest> _validator;
    private readonly ISessionRepository _sessionRepository;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public RestartInventoryRunHandler(
        IValidator<RestartRunRequest> validator,
        ISessionRepository sessionRepository,
        IAuditLogger auditLogger,
        IClock clock)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IResult> HandleAsync(
        Guid locationId,
        RestartRunRequest? request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return EndpointUtilities.Problem(
                "Requête invalide",
                "Le corps de la requête est requis.",
                StatusCodes.Status400BadRequest);
        }

        var validationResult = await _validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validationResult.IsValid)
        {
            return EndpointUtilities.ValidationProblem(validationResult);
        }

        var parameters = new RestartRunParameters
        {
            LocationId = locationId,
            OwnerUserId = request.OwnerUserId,
            CountType = request.CountType,
            RestartedAtUtc = _clock.UtcNow
        };

        var result = await _sessionRepository.RestartRunAsync(parameters, cancellationToken).ConfigureAwait(false);
        if (result.Error is not null)
        {
            return ToProblem(result.Error);
        }

        var run = result.Run!;
        var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
        var actor = EndpointUtilities.FormatActorLabel(httpContext);
        var timestamp = EndpointUtilities.FormatTimestamp(run.RestartedAtUtc);
        var zoneDescription = BuildZoneDescription(run);
        var countDescription = EndpointUtilities.DescribeCountType(run.CountType);
        var resultDetails = run.ClosedRuns > 0
            ? "et clôturé les comptages actifs"
            : "mais aucun comptage actif n'était ouvert";
        var message = $"{actor} a relancé {zoneDescription} pour un {countDescription} le {timestamp} UTC {resultDetails}.";

        await _auditLogger.LogAsync(message, userName, "inventories.restart", cancellationToken).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static string BuildZoneDescription(RestartRunInfo runInfo)
    {
        if (!string.IsNullOrWhiteSpace(runInfo.LocationCode))
        {
            return $"la zone {runInfo.LocationCode} – {runInfo.LocationLabel}";
        }

        if (!string.IsNullOrWhiteSpace(runInfo.LocationLabel))
        {
            return $"la zone {runInfo.LocationLabel}";
        }

        return $"la zone {runInfo.LocationId}";
    }

    private static IResult ToProblem(RepositoryError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        IDictionary<string, object?>? extensions = null;
        if (error.Metadata is { Count: > 0 })
        {
            extensions = new Dictionary<string, object?>(error.Metadata, StringComparer.Ordinal);
        }

        return EndpointUtilities.Problem(error.Title, error.Detail, error.StatusCode, extensions);
    }
}
