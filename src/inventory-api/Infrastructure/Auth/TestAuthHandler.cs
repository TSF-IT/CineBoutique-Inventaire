using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CineBoutique.Inventory.Api.Infrastructure.Auth;

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock) : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var auth = Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(auth) ||
            !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            // Pas de jeton => laissez le pipeline répondre 401
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = auth.Substring("Bearer ".Length).Trim();

        // On tolère plusieurs formats:
        //   "admin", "operator", "viewer"
        //   "admin:GUID", "operator-GUID", "viewer GUID", etc.
        var parts = token.Split(new[] { ':', '-', '|', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var head  = parts.FirstOrDefault()?.ToLowerInvariant();

        var role = head switch
        {
            "admin"    => "admin",
            "operator" => "operator",
            "viewer"   => "viewer",
            _          => null
        };

        if (role is null)
            return Task.FromResult(AuthenticateResult.Fail("Unknown test token"));

        // Récupère un userId s’il est fourni (dans le token, un header ou la query)
        string? userId = parts.Skip(1).FirstOrDefault(p => Guid.TryParse(p, out _));
        userId ??= Request.Headers["X-Test-UserId"].FirstOrDefault()
                ?? Request.Headers["X-User-Id"].FirstOrDefault()
                ?? Request.Query["userId"].FirstOrDefault()
                ?? Guid.NewGuid().ToString(); // fallback

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, $"{role}-user"),
            new(ClaimTypes.Role, role) // ⚠️ minuscules, cohérent avec tes policies
        };

        var identity  = new ClaimsIdentity(claims, Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
