using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CineBoutique.Inventory.Api.Infrastructure.Authentication;

public sealed class AdminHeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AdminHeader";

    public AdminHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>();

        if (TryReadAdminHeader(out var isAdmin) && isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            claims.Add(new Claim("is_admin", "true"));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private bool TryReadAdminHeader(out bool isAdmin)
    {
        isAdmin = false;

        if (!Request.Headers.TryGetValue("X-Admin", out var values))
        {
            return false;
        }

        foreach (var value in values)
        {
            if (string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "1", System.StringComparison.OrdinalIgnoreCase))
            {
                isAdmin = true;
                return true;
            }
        }

        return true;
    }
}
