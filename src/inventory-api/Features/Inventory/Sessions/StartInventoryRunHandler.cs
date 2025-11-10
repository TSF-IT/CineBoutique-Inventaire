using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Infrastructure.Database.Inventory;
using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace CineBoutique.Inventory.Api.Features.Inventory.Sessions;

internal sealed class StartInventoryRunHandler
{
    private readonly IValidator<StartRunRequest> _validator;
    private readonly ISessionRepository _sessionRepository;

    public StartInventoryRunHandler(
        IValidator<StartRunRequest> validator,
        ISessionRepository sessionRepository)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
    }

    public async Task<IResult> HandleAsync(Guid locationId, StartRunRequest? request, CancellationToken cancellationToken)
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

        var startResult = await _sessionRepository
            .StartRunAsync(
                new StartRunParameters
                {
                    LocationId = locationId,
                    ShopId = request.ShopId,
                    OwnerUserId = request.OwnerUserId,
                    CountType = request.CountType
                },
                cancellationToken)
            .ConfigureAwait(false);

        return startResult.Status switch
        {
            StartRunStatus.LocationNotFound => EndpointUtilities.Problem(
                "Ressource introuvable",
                "La zone demandée est introuvable.",
                StatusCodes.Status404NotFound),
            StartRunStatus.LocationDisabled => EndpointUtilities.Problem(
                "Zone désactivée",
                "La zone demandée est désactivée et ne peut pas démarrer de comptage.",
                StatusCodes.Status409Conflict),
            StartRunStatus.OwnerInvalid => EndpointUtilities.Problem(
                "Requête invalide",
                "ownerUserId n'appartient pas à la boutique fournie ou est désactivé.",
                StatusCodes.Status400BadRequest,
                new Dictionary<string, object?>
                {
                    [nameof(startResult.OwnerUserId)] = startResult.OwnerUserId,
                    [nameof(startResult.ShopId)] = startResult.ShopId
                }),
            StartRunStatus.SequentialPrerequisiteMissing => EndpointUtilities.Problem(
                "Pré-requis manquant",
                "Terminez le comptage n°1 avant de lancer le comptage n°2.",
                StatusCodes.Status409Conflict),
            StartRunStatus.ConflictOtherOwner => EndpointUtilities.Problem(
                "Conflit",
                $"Comptage déjà en cours par {FormatOwnerLabel(startResult.ConflictingOwnerLabel)}.",
                StatusCodes.Status409Conflict),
            StartRunStatus.Success when startResult.Run is not null => Results.Ok(new StartInventoryRunResponse
            {
                RunId = startResult.Run.RunId,
                InventorySessionId = startResult.Run.InventorySessionId,
                LocationId = startResult.Run.LocationId,
                CountType = startResult.Run.CountType,
                OwnerUserId = startResult.Run.OwnerUserId,
                OwnerDisplayName = startResult.Run.OwnerDisplayName,
                OperatorDisplayName = startResult.Run.OperatorDisplayName,
                StartedAtUtc = TimeUtil.ToUtcOffset(startResult.Run.StartedAtUtc)
            }),
            _ => Results.Problem(
                "Erreur inattendue",
                "Une erreur est survenue lors du démarrage du run.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static string FormatOwnerLabel(string? label) =>
        string.IsNullOrWhiteSpace(label) ? "un autre utilisateur" : label.Trim();
}

