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

internal static class ShopUsersEndpoints
{
    public static IEndpointRouteBuilder MapShopUsersEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/shops/{shopId:guid}/users")
            .WithTags("ShopUsers");

        MapGetShopUsersEndpoint(group);
        MapCreateShopUserEndpoint(group);
        MapUpdateShopUserEndpoint(group);
        MapDeleteShopUserEndpoint(group);

        return app;
    }

    private static void MapGetShopUsersEndpoint(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet(
                string.Empty,
                async (
                    Guid shopId,
                    bool includeDisabled,
                    [FromServices] IShopUserService shopUserService,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var users = await shopUserService.GetAsync(shopId, includeDisabled, cancellationToken).ConfigureAwait(false);
                        return Results.Ok(users);
                    }
                    catch (ShopNotFoundException ex)
                    {
                        return Results.NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
                    }
                })
            .Produces<IReadOnlyList<ShopUserDto>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private static void MapCreateShopUserEndpoint(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost(
                string.Empty,
                async (
                    Guid shopId,
                    [FromBody] CreateShopUserRequest? request,
                    [FromServices] IShopUserService shopUserService,
                    [FromServices] IAuditLogger auditLogger,
                    [FromServices] IClock clock,
                    [FromServices] IValidator<CreateShopUserRequest> validator,
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
                        var created = await shopUserService.CreateAsync(shopId, request, cancellationToken).ConfigureAwait(false);

                        await LogUserChangeAsync(
                                shopId,
                                $"a créé l'utilisateur '{created.Login}' ({created.Id}).",
                                "shop_users.create.success",
                                clock,
                                auditLogger,
                                httpContext,
                                cancellationToken)
                            .ConfigureAwait(false);

                        return Results.Created($"/api/shops/{shopId}/users/{created.Id}", created);
                    }
                    catch (ShopNotFoundException ex)
                    {
                        return Results.NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
                    }
                    catch (ShopUserConflictException ex)
                    {
                        return Results.Conflict(BuildConflictProblem(ex.Message));
                    }
                })
            .Produces<ShopUserDto>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization("Admin");
    }

    private static void MapUpdateShopUserEndpoint(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPut(
                string.Empty,
                async (
                    Guid shopId,
                    [FromBody] UpdateShopUserRequest? request,
                    [FromServices] IShopUserService shopUserService,
                    [FromServices] IAuditLogger auditLogger,
                    [FromServices] IClock clock,
                    [FromServices] IValidator<UpdateShopUserRequest> validator,
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
                        var updated = await shopUserService.UpdateAsync(shopId, request, cancellationToken).ConfigureAwait(false);

                        await LogUserChangeAsync(
                                shopId,
                                $"a mis à jour l'utilisateur '{updated.Login}' ({updated.Id}).",
                                "shop_users.update.success",
                                clock,
                                auditLogger,
                                httpContext,
                                cancellationToken)
                            .ConfigureAwait(false);

                        return Results.Ok(updated);
                    }
                    catch (ShopNotFoundException ex)
                    {
                        return Results.NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
                    }
                    catch (ShopUserNotFoundException ex)
                    {
                        return Results.NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
                    }
                    catch (ShopUserConflictException ex)
                    {
                        return Results.Conflict(BuildConflictProblem(ex.Message));
                    }
                })
            .Produces<ShopUserDto>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization("Admin");
    }

    private static void MapDeleteShopUserEndpoint(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapDelete(
                string.Empty,
                async (
                    Guid shopId,
                    [FromBody] DeleteShopUserRequest? request,
                    [FromServices] IShopUserService shopUserService,
                    [FromServices] IAuditLogger auditLogger,
                    [FromServices] IClock clock,
                    [FromServices] IValidator<DeleteShopUserRequest> validator,
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
                        var disabled = await shopUserService.SoftDeleteAsync(shopId, request, cancellationToken).ConfigureAwait(false);

                        await LogUserChangeAsync(
                                shopId,
                                $"a désactivé l'utilisateur '{disabled.Login}' ({disabled.Id}).",
                                "shop_users.delete.success",
                                clock,
                                auditLogger,
                                httpContext,
                                cancellationToken)
                            .ConfigureAwait(false);

                        return Results.Ok(disabled);
                    }
                    catch (ShopNotFoundException ex)
                    {
                        return Results.NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
                    }
                    catch (ShopUserNotFoundException ex)
                    {
                        return Results.NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
                    }
                })
            .Produces<ShopUserDto>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Admin");
    }

    private static async Task LogUserChangeAsync(
        Guid shopId,
        string actionDescription,
        string category,
        IClock clock,
        IAuditLogger auditLogger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userName = EndpointUtilities.GetAuthenticatedUserName(httpContext);
        var actor = EndpointUtilities.FormatActorLabel(httpContext);
        var timestamp = EndpointUtilities.FormatTimestamp(clock.UtcNow);
        var message = $"{actor} {actionDescription} Boutique {shopId}. Le {timestamp} UTC.";

        await auditLogger.LogAsync(message, userName, category, cancellationToken).ConfigureAwait(false);
    }

    private static ProblemDetails BuildProblem(string detail, int statusCode = StatusCodes.Status409Conflict) => new()
    {
        Status = statusCode,
        Title = "Requête invalide",
        Detail = detail
    };

    private static ProblemDetails BuildConflictProblem(string detail) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Identifiant déjà utilisé",
        Detail = detail
    };
}
