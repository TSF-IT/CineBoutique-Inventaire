using CineBoutique.Inventory.Api.Auth;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Audit;
using CineBoutique.Inventory.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CineBoutique.Inventory.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IShopUserAuthenticationService _authenticationService;
    private readonly ITokenService _tokenService;
    private readonly IAuditLogger _auditLogger;

    public AuthController(
        IShopUserAuthenticationService authenticationService,
        ITokenService tokenService,
        IAuditLogger auditLogger)
    {
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await _authenticationService
            .AuthenticateAsync(request.ShopId, request.Login, request.Secret, cancellationToken)
            .ConfigureAwait(false);

        var timestamp = EndpointUtilities.FormatTimestamp(DateTimeOffset.UtcNow);

        if (result.Succeeded && result.User is not null)
        {
            var token = _tokenService.GenerateToken(result.User);

            var actor = EndpointUtilities.FormatActorLabel(result.User.DisplayName);
            await _auditLogger.LogAsync(
                    $"{actor} (login '{result.User.Login}') s'est connecté avec succès à la boutique {result.User.ShopId} le {timestamp} UTC.",
                    result.User.DisplayName,
                    "auth.login.success",
                    cancellationToken)
                .ConfigureAwait(false);

            var response = new LoginResponse(
                result.User.ShopId,
                result.User.UserId,
                result.User.Login,
                result.User.DisplayName,
                result.User.IsAdmin,
                token.AccessToken,
                token.ExpiresAtUtc);

            return Ok(response);
        }

        await LogFailureAsync(request, result.Status, timestamp, cancellationToken).ConfigureAwait(false);

        return Unauthorized();
    }

    private Task LogFailureAsync(LoginRequest request, ShopUserAuthenticationStatus status, string timestamp, CancellationToken cancellationToken)
    {
        var reason = status switch
        {
            ShopUserAuthenticationStatus.UserNotFound => "utilisateur introuvable",
            ShopUserAuthenticationStatus.SecretRequired => "secret absent",
            ShopUserAuthenticationStatus.InvalidSecret => "secret invalide",
            ShopUserAuthenticationStatus.SecretNotConfigured => "secret non configuré",
            ShopUserAuthenticationStatus.UnsupportedSecret => "algorithme de hash non supporté",
            ShopUserAuthenticationStatus.UserDisabled => "utilisateur désactivé",
            _ => "erreur inconnue"
        };

        var message = $"Tentative de connexion refusée pour le login '{request.Login}' sur la boutique {request.ShopId} le {timestamp} UTC ({reason}).";
        return _auditLogger.LogAsync(message, null, "auth.login.failure", cancellationToken);
    }
}
