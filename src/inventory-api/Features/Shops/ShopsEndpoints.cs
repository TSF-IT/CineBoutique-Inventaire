using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Services;
using CineBoutique.Inventory.Api.Services.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CineBoutique.Inventory.Api.Features.Shops;

internal static class ShopsEndpoints
{
    public static IEndpointRouteBuilder MapShopsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/shops")
            .WithTags("Shops");

        MapGetShopsEndpoint(group);
        MapCreateShopEndpoint(group);
        MapUpdateShopEndpoint(group);
        MapDeleteShopEndpoint(group);

        return app;
    }

    private static void MapGetShopsEndpoint(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet(
                string.Empty,
                async (string? kind, IShopService shopService, CancellationToken cancellationToken) =>
                {
                    var shops = await shopService.GetAsync(kind, cancellationToken).ConfigureAwait(false);
                    return Results.Ok(shops);
                })
            .WithName("GetShops")
            .Produces<IReadOnlyList<ShopDto>>(StatusCodes.Status200OK)
            .RequireAuthorization();
    }

    private static void MapCreateShopEndpoint(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost(
                string.Empty,
                async (
                    [FromBody] CreateShopRequest? request,
                    [FromServices] IShopService shopService,
                    [FromServices] IAuditLogger auditLogger,
                    [FromServices] IClock clock,
                    [FromServices] IValidator<CreateShopRequest> validator,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    if (request is null)
                    {
                        return EndpointUtilities.Problem(
                            "Requête invalide",
                            "Le corps de la requête est requis.",
                            StatusCodes.Status400BadRequest);
                    }

                    var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
                    if (!validationResult.IsValid)
                    {
                        return EndpointUtilities.ValidationProblem(validationResult);
                    }

                    try
                    {
                        var created = await shopService.CreateAsync(request, cancellationToken).ConfigureAwait(false);

                        await LogShopChangeAsync(
                                clock,
                                auditLogger,
                                httpContext,
                                $"a créé la boutique '{created.Name}' ({created.Id}).",
                                "shops.create.success",
                                cancellationToken)
                            .ConfigureAwait(false);

                        return Results.Created($"/api/shops/{created.Id}", created);
                    }
                    catch (ShopConflictException ex)
                    {
                        return Results.Conflict(BuildProblem(ex.Message));
                    }
                })
            .WithName("CreateShop")
            .Produces<ShopDto>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization("Admin");
    }

    private static void MapUpdateShopEndpoint(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPut(
                string.Empty,
                async (
                    [FromBody] UpdateShopRequest? request,
                    [FromServices] IShopService shopService,
                    [FromServices] IAuditLogger auditLogger,
                    [FromServices] IClock clock,
                    [FromServices] IValidator<UpdateShopRequest> validator,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    if (request is null)
                    {
                        return EndpointUtilities.Problem(
                            "Requête invalide",
                            "Le corps de la requête est requis.",
                            StatusCodes.Status400BadRequest);
                    }

                    var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
                    if (!validationResult.IsValid)
                    {
                        return EndpointUtilities.ValidationProblem(validationResult);
                    }

                    try
                    {
                        var updated = await shopService.UpdateAsync(request, cancellationToken).ConfigureAwait(false);

                        await LogShopChangeAsync(
                                clock,
                                auditLogger,
                                httpContext,
                                $"a renommé la boutique {updated.Id} en '{updated.Name}'.",
                                "shops.update.success",
                                cancellationToken)
                            .ConfigureAwait(false);

                        return Results.Ok(updated);
                    }
                    catch (ShopNotFoundException ex)
                    {
                        return Results.NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
                    }
                    catch (ShopConflictException ex)
                    {
                        return Results.Conflict(BuildProblem(ex.Message));
                    }
                })
            .WithName("UpdateShop")
            .Produces<ShopDto>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization("Admin");
    }

    private static void MapDeleteShopEndpoint(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapDelete(
                string.Empty,
                async (
                    [FromBody] DeleteShopRequest? request,
                    [FromServices] IShopService shopService,
                    [FromServices] IAuditLogger auditLogger,
                    [FromServices] IClock clock,
                    [FromServices] IValidator<DeleteShopRequest> validator,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    if (request is null)
                    {
                        return EndpointUtilities.Problem(
                            "Requête invalide",
                            "Le corps de la requête est requis.",
                            StatusCodes.Status400BadRequest);
                    }

                    var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
                    if (!validationResult.IsValid)
                    {
                        return EndpointUtilities.ValidationProblem(validationResult);
                    }

                    try
                    {
                        var deleted = await shopService.DeleteAsync(request, cancellationToken).ConfigureAwait(false);

                        await LogShopChangeAsync(
                                clock,
                                auditLogger,
                                httpContext,
                                $"a supprimé la boutique '{deleted.Name}' ({deleted.Id}).",
                                "shops.delete.success",
                                cancellationToken)
                            .ConfigureAwait(false);

                        return Results.NoContent();
                    }
                    catch (ShopNotFoundException ex)
                    {
                        return Results.NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
                    }
                    catch (ShopNotEmptyException ex)
                    {
                        return Results.Conflict(BuildProblem(ex.Message));
                    }
                })
            .WithName("DeleteShop")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization("Admin");
    }

    private static async Task LogShopChangeAsync(
        IClock clock,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        string actionDescription,
        string category,
        CancellationToken cancellationToken)
    {
        var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
        var actor = EndpointUtilities.FormatActorLabel(httpContext);
        var timestamp = EndpointUtilities.FormatTimestamp(clock.UtcNow);
        var message = $"{actor} {actionDescription} Le {timestamp} UTC.";

        await auditLogger.LogAsync(message, userName, category, cancellationToken).ConfigureAwait(false);
    }

    private static ProblemDetails BuildProblem(string detail, int statusCode = StatusCodes.Status409Conflict) => new()
    {
        Status = statusCode,
        Title = "Requête invalide",
        Detail = detail
    };
}
