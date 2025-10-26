using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Api.Infrastructure.Authentication;

public sealed class AdminHeaderAuthenticationHandler : AuthenticationHandler<AdminHeaderAuthenticationOptions>
{
    public const string SchemeName = "AdminHeader";

    public AdminHeaderAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AdminHeaderAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!string.IsNullOrEmpty(Options.AppToken) && !IsValidAppToken())
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing or invalid X-App-Token header."));
        }

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

    private bool IsValidAppToken()
    {
        if (!Request.Headers.TryGetValue("X-App-Token", out var values))
        {
            return false;
        }

        foreach (var value in values)
        {
            if (string.Equals(value, Options.AppToken, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
