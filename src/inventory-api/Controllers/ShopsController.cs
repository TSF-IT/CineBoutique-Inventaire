using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Services;
using CineBoutique.Inventory.Api.Services.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CineBoutique.Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ShopsController : ControllerBase
{
    private readonly IShopService _shopService;
    private readonly IAuditLogger _auditLogger;
    private readonly IClock _clock;

    public ShopsController(IShopService shopService, IAuditLogger auditLogger, IClock clock)
    {
        _shopService = shopService ?? throw new ArgumentNullException(nameof(shopService));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShopDto>>> GetAsync(CancellationToken cancellationToken)
    {
        var shops = await _shopService.GetAsync(cancellationToken).ConfigureAwait(false);
        return Ok(shops);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShopDto>> CreateAsync([FromBody] CreateShopRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _shopService.CreateAsync(request, cancellationToken).ConfigureAwait(false);

            await LogShopChangeAsync(
                $"a créé la boutique '{created.Name}' ({created.Id}).",
                "shops.create.success",
                cancellationToken).ConfigureAwait(false);

            return Created($"/api/shops/{created.Id}", created);
        }
        catch (ShopConflictException ex)
        {
            return Conflict(BuildProblem(ex.Message));
        }
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShopDto>> UpdateAsync([FromBody] UpdateShopRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _shopService.UpdateAsync(request, cancellationToken).ConfigureAwait(false);

            await LogShopChangeAsync(
                $"a renommé la boutique {updated.Id} en '{updated.Name}'.",
                "shops.update.success",
                cancellationToken).ConfigureAwait(false);

            return Ok(updated);
        }
        catch (ShopNotFoundException ex)
        {
            return NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
        }
        catch (ShopConflictException ex)
        {
            return Conflict(BuildProblem(ex.Message));
        }
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync([FromBody] DeleteShopRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _shopService.DeleteAsync(request, cancellationToken).ConfigureAwait(false);

            await LogShopChangeAsync(
                $"a supprimé la boutique '{deleted.Name}' ({deleted.Id}).",
                "shops.delete.success",
                cancellationToken).ConfigureAwait(false);

            return NoContent();
        }
        catch (ShopNotFoundException ex)
        {
            return NotFound(BuildProblem(ex.Message, StatusCodes.Status404NotFound));
        }
        catch (ShopNotEmptyException ex)
        {
            return Conflict(BuildProblem(ex.Message));
        }
    }

    private async Task LogShopChangeAsync(string actionDescription, string category, CancellationToken cancellationToken)
    {
        var userName = EndpointUtilities.GetAuthenticatedUserName(HttpContext);
        var actor = EndpointUtilities.FormatActorLabel(HttpContext);
        var timestamp = EndpointUtilities.FormatTimestamp(_clock.UtcNow);
        var message = $"{actor} {actionDescription} Le {timestamp} UTC.";

        await _auditLogger.LogAsync(message, userName, category, cancellationToken).ConfigureAwait(false);
    }

    private static ProblemDetails BuildProblem(string detail, int statusCode = StatusCodes.Status409Conflict) => new()
    {
        Status = statusCode,
        Title = "Requête invalide",
        Detail = detail
    };
}
