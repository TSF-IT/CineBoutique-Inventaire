using System;
using System.Linq;
using CineBoutique.Inventory.Api.Models.Admin;
using CineBoutique.Inventory.Domain.Admin;
using CineBoutique.Inventory.Domain.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace CineBoutique.Inventory.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Produces("application/json")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IAdminUserRepository _repository;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        IAdminUserRepository repository,
        IAuditLogger auditLogger,
        ILogger<AdminUsersController> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    [ProducesResponseType(typeof(AdminUserListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminUserListResponse>> SearchAsync([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _repository.SearchAsync(q, page, pageSize, cancellationToken).ConfigureAwait(false);

        var payload = new AdminUserListResponse(
            result.Items.Select(Map).ToList(),
            result.TotalCount,
            result.Page,
            result.PageSize);

        await _auditLogger.LogAsync(
            new AuditEntry("AdminUser", "LIST", "List", new { Query = q, Page = page, PageSize = pageSize }, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        return Ok(payload);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        var entry = new AuditEntry("AdminUser", id.ToString(), user is null ? "NotFound" : "Get", null, DateTimeOffset.UtcNow);
        await _auditLogger.LogAsync(entry, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return NotFound();
        }

        return Ok(Map(user));
    }

    [HttpPost]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> CreateAsync([FromBody] AdminUserCreateRequest? request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateAdminUser requested: {Email}", request?.Email);

        if (request is null)
        {
            return BadRequest("Request body is required");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("Email is required");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return BadRequest("Display name is required");
        }

        var email = request.Email.Trim();
        var displayName = request.DisplayName.Trim();

        try
        {
            var created = await _repository.CreateAsync(email, displayName, cancellationToken).ConfigureAwait(false);

            var dto = Map(created);

            try
            {
                await _auditLogger.LogAsync(
                    new AuditEntry("AdminUser", created.Id.ToString(), "Create", new { dto.Email, dto.DisplayName }, DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception auditEx)
            {
                _logger.LogWarning(auditEx, "Audit failed on CreateAdminUser");
            }

            return CreatedAtAction(nameof(GetByIdAsync), new { id = dto.Id }, dto);
        }
        catch (DuplicateUserException ex)
        {
            _logger.LogWarning(ex, "Création d'utilisateur admin en conflit pour {Email}", email);
            return Conflict(new { message = ex.Message });
        }
        catch (PostgresException ex)
        {
            _logger.LogWarning(ex, "Erreur SQL lors de la création de l'utilisateur admin {Email}", email);
            return BadRequest(new { message = $"Database error while creating admin user: {ex.MessageText}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating admin user for {Email}", email);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Unexpected error while creating admin user." });
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> UpdateAsync(Guid id, [FromBody] AdminUserUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var updated = await _repository.UpdateAsync(id, request.Email.Trim(), request.DisplayName.Trim(), cancellationToken).ConfigureAwait(false);

            if (updated is null)
            {
                await _auditLogger.LogAsync(
                    new AuditEntry("AdminUser", id.ToString(), "NotFound", new { request.Email, request.DisplayName }, DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                return NotFound();
            }

            await _auditLogger.LogAsync(
                new AuditEntry("AdminUser", id.ToString(), "Update", new { updated.Email, updated.DisplayName }, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);

            return Ok(Map(updated));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Mise à jour d'utilisateur admin en conflit pour {Email}", request.Email);
            await _auditLogger.LogAsync(
                new AuditEntry("AdminUser", id.ToString(), "Conflict", new { request.Email }, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry("AdminUser", id.ToString(), deleted ? "Delete" : "NotFound", null, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    private static AdminUserDto Map(AdminUser user)
    {
        return new AdminUserDto(user.Id, user.Email, user.DisplayName, user.CreatedAtUtc, user.UpdatedAtUtc);
    }
}
