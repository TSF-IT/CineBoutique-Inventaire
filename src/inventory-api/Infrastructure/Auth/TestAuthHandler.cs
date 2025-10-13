using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CineBoutique.Inventory.Api.Infrastructure.Auth;

public static class Roles
{
    public const string Viewer = "Viewer";
    public const string Operator = "Operator";
    public const string Admin = "Admin";
}

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public new const string Scheme = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? role = null;

        if (Request.Headers.TryGetValue("Authorization", out var auth))
        {
            var parts = auth.ToString().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                var scheme = parts[0];
                var token  = parts[1];
                if (scheme.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
                    scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                {
                    role = token;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(role) &&
            Request.Headers.TryGetValue("X-Test-Role", out var roleHeader))
        {
            role = roleHeader.ToString();
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            // Pas d’info d’auth => l’AuthorizationMiddleware retournera 401 si l’endpoint exige [Authorize]/RequireAuthorization.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var normalized = role.Trim().ToLowerInvariant();
        var roleName = normalized switch
        {
            "viewer"   => Roles.Viewer,
            "operator" or "op" => Roles.Operator,
            "admin" or "administrator" => Roles.Admin,
            _ => Roles.Viewer
        };

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, $"test-{roleName.ToLowerInvariant()}"),
            new Claim(ClaimTypes.Role, roleName),
        };

        var identity  = new ClaimsIdentity(claims, Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
