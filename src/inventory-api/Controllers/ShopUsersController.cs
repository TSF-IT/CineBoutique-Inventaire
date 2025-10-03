using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Services;
using CineBoutique.Inventory.Api.Services.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CineBoutique.Inventory.Api.Controllers;

[ApiController]
[Route("api/shops/{shopId:guid}/users")]
public sealed class ShopUsersController : ControllerBase
{
    private readonly IShopUserService _shopUserService;
    private readonly IAuditLogger _auditLogger;

    public ShopUsersController(IShopUserService shopUserService, IAuditLogger auditLogger)
    {
        _shopUserService = shopUserService ?? throw new ArgumentNullException(nameof(shopUserService));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ShopUserDto>>> GetAsync(Guid shopId, CancellationToken cancellationToken)
    {
        try
        {
            var users = await _shopUserService.GetAsync(shopId, cancellationToken).ConfigureAwait(false);
            return Ok(users);
        }
        catch (ShopNotFoundException ex)
        {
            return NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
        }
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShopUserDto>> CreateAsync(Guid shopId, [FromBody] CreateShopUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _shopUserService.CreateAsync(shopId, request, cancellationToken).ConfigureAwait(false);

            await LogUserChangeAsync(
                shopId,
                $"a créé l'utilisateur '{created.Login}' ({created.Id}).",
                "shop_users.create.success",
                cancellationToken).ConfigureAwait(false);

            return Created($"/api/shops/{shopId}/users/{created.Id}", created);
        }
        catch (ShopNotFoundException ex)
        {
            return NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
        }
        catch (ShopUserConflictException ex)
        {
            return Conflict(BuildProblem(ex.Message));
        }
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShopUserDto>> UpdateAsync(Guid shopId, [FromBody] UpdateShopUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _shopUserService.UpdateAsync(shopId, request, cancellationToken).ConfigureAwait(false);

            await LogUserChangeAsync(
                shopId,
                $"a mis à jour l'utilisateur '{updated.Login}' ({updated.Id}).",
                "shop_users.update.success",
                cancellationToken).ConfigureAwait(false);

            return Ok(updated);
        }
        catch (ShopNotFoundException ex)
        {
            return NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
        }
        catch (ShopUserNotFoundException ex)
        {
            return NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
        }
        catch (ShopUserConflictException ex)
        {
            return Conflict(BuildProblem(ex.Message));
        }
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShopUserDto>> DeleteAsync(Guid shopId, [FromBody] DeleteShopUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var disabled = await _shopUserService.SoftDeleteAsync(shopId, request, cancellationToken).ConfigureAwait(false);

            await LogUserChangeAsync(
                shopId,
                $"a désactivé l'utilisateur '{disabled.Login}' ({disabled.Id}).",
                "shop_users.delete.success",
                cancellationToken).ConfigureAwait(false);

            return Ok(disabled);
        }
        catch (ShopNotFoundException ex)
        {
            return NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
        }
        catch (ShopUserNotFoundException ex)
        {
            return NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
        }
    }

    private async Task LogUserChangeAsync(
        Guid shopId,
        string actionDescription,
        string category,
        CancellationToken cancellationToken)
    {
        var userName = EndpointUtilities.GetAuthenticatedUserName(HttpContext);
        var actor = EndpointUtilities.FormatActorLabel(userName);
        var timestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);
        var message = $"{actor} {actionDescription} Boutique {shopId}. Le {timestamp} UTC.";

        await _auditLogger.LogAsync(message, userName, category, cancellationToken).ConfigureAwait(false);
    }

    private static ProblemDetails BuildProblem(string detail, int statusCode = StatusCodes.Status409Conflict) => new()
    {
        Status = statusCode,
        Title = "Requête invalide",
        Detail = detail
    };
}
