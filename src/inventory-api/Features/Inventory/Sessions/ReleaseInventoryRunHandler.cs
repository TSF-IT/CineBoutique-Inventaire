using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database.Inventory;
using FluentValidation;

namespace CineBoutique.Inventory.Api.Features.Inventory.Sessions;

internal sealed class ReleaseInventoryRunHandler(
    IValidator<ReleaseRunRequest> validator,
    ISessionRepository sessionRepository)
{
    private readonly IValidator<ReleaseRunRequest> _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly ISessionRepository _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));

    public async Task<IResult> HandleAsync(Guid locationId, ReleaseRunRequest? request, CancellationToken cancellationToken)
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
            return EndpointUtilities.ValidationProblem(validationResult);

        var result = await _sessionRepository
            .ReleaseRunAsync(
                new ReleaseRunParameters
                {
                    LocationId = locationId,
                    OwnerUserId = request.OwnerUserId,
                    RunId = request.RunId
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Error is not null)
            return ToProblem(result.Error);

        return Results.NoContent();
    }

    private static IResult ToProblem(RepositoryError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        IDictionary<string, object?>? extensions = null;
        if (error.Metadata is { Count: > 0 })
            extensions = new Dictionary<string, object?>(error.Metadata, StringComparer.Ordinal);

        return EndpointUtilities.Problem(error.Title, error.Detail, error.StatusCode, extensions);
    }
}
