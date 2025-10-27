using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Validation;
using CineBoutique.Inventory.Infrastructure.Database.Inventory;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace CineBoutique.Inventory.Api.Features.Inventory.Sessions;

internal sealed class CompleteInventoryRunHandler
{
    private readonly IValidator<CompleteRunRequest> _validator;
    private readonly ISessionRepository _sessionRepository;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public CompleteInventoryRunHandler(
        IValidator<CompleteRunRequest> validator,
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
        CompleteRunRequest? request,
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

        var rawItems = request.Items?.ToArray() ?? Array.Empty<CompleteRunItemRequest>();
        var sanitizedItems = new List<SanitizedLine>(rawItems.Length);
        var additionalFailures = new List<ValidationFailure>();

        for (var index = 0; index < rawItems.Length; index++)
        {
            var item = rawItems[index];
            var sanitizedEan = InventoryCodeValidator.Normalize(item.Ean);
            if (sanitizedEan is null)
            {
                additionalFailures.Add(new ValidationFailure($"items[{index}].ean", "Chaque ligne doit contenir un code produit."));
                continue;
            }

            if (!InventoryCodeValidator.TryValidate(sanitizedEan, out var eanError))
            {
                additionalFailures.Add(new ValidationFailure($"items[{index}].ean", eanError));
                continue;
            }

            if (item.Quantity < 0)
            {
                additionalFailures.Add(new ValidationFailure($"items[{index}].quantity", $"La quantité pour le code {sanitizedEan} doit être positive ou nulle."));
                continue;
            }

            sanitizedItems.Add(new SanitizedLine(sanitizedEan, item.Quantity, item.IsManual));
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

        var aggregatedItems = sanitizedItems
            .GroupBy(line => line.Ean, StringComparer.Ordinal)
            .Select(group => new SanitizedCountLineModel
            {
                Ean = group.Key,
                Quantity = group.Sum(line => line.Quantity),
                IsManual = group.Any(line => line.IsManual)
            })
            .ToList();

        var completedAt = _clock.UtcNow;
        var parameters = new CompleteRunParameters
        {
            LocationId = locationId,
            OwnerUserId = request.OwnerUserId,
            CountType = request.CountType,
            RunId = request.RunId,
            CompletedAtUtc = completedAt,
            Items = aggregatedItems
        };

        var result = await _sessionRepository.CompleteRunAsync(parameters, cancellationToken).ConfigureAwait(false);
        if (result.Error is not null)
        {
            return ToProblem(result.Error);
        }

        var run = result.Run!;
        var response = new CompleteInventoryRunResponse
        {
            RunId = run.RunId,
            InventorySessionId = run.InventorySessionId,
            LocationId = run.LocationId,
            CountType = run.CountType,
            CompletedAtUtc = run.CompletedAtUtc,
            ItemsCount = run.ItemsCount,
            TotalQuantity = run.TotalQuantity
        };

        var actor = EndpointUtilities.FormatActorLabel(httpContext);
        var timestamp = EndpointUtilities.FormatTimestamp(run.CompletedAtUtc);
        var zoneDescription = string.IsNullOrWhiteSpace(run.LocationCode)
            ? run.LocationLabel
            : $"{run.LocationCode} – {run.LocationLabel}";
        var countDescription = EndpointUtilities.DescribeCountType(run.CountType);
        var auditMessage =
            $"{actor} a terminé {zoneDescription} pour un {countDescription} le {timestamp} UTC ({run.ItemsCount} références, total {run.TotalQuantity}).";

        await _auditLogger.LogAsync(auditMessage, run.OwnerDisplayName, "inventories.complete.success", cancellationToken).ConfigureAwait(false);

        return Results.Ok(response);
    }

    private static IResult ToProblem(RepositoryError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        IDictionary<string, object?>? extensions = null;
        if (error.Metadata is { Count: > 0 })
        {
            extensions = new Dictionary<string, object?>(error.Metadata, StringComparer.Ordinal);
        }

        return EndpointUtilities.Problem(
            error.Title,
            error.Detail,
            error.StatusCode,
            extensions);
    }

    private sealed record SanitizedLine(string Ean, decimal Quantity, bool IsManual);
}
