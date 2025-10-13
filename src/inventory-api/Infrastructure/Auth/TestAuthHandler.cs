using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace CineBoutique.Inventory.Api.Infrastructure.Auth;

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string TestIssuer = "cineboutique-test";
    private const string TestAudience = "cineboutique-web";
    private const string TestSigningKey = "insecure-test-key-32bytes-minimum!!!!";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock) : base(options, logger, encoder, clock) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1) Bearer token ?
        if (Request.Headers.TryGetValue("Authorization", out var authValues))
        {
            var auth = authValues.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = auth.Substring("Bearer ".Length).Trim();

                // a) Essai avec les constantes de test utilisées par TestTokenFactory
                var principal = TryValidate(token, TestIssuer, TestAudience, TestSigningKey);
                if (principal == null)
                {
                    // b) Repli : tenter avec la configuration CI si elle diffère
                    var cfg = Context.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
                    var issuer = cfg?["Jwt:Issuer"] ?? cfg?["Authentication:Jwt:Issuer"] ?? TestIssuer;
                    var audience = cfg?["Jwt:Audience"] ?? cfg?["Authentication:Jwt:Audience"] ?? TestAudience;
                    var key = cfg?["Jwt:SigningKey"] ?? cfg?["Authentication:Jwt:SigningKey"] ?? TestSigningKey;

                    principal = TryValidate(token, issuer!, audience!, key!);
                }

                if (principal != null)
                {
                    // S'assure d'avoir une AuthenticationType non vide
                    var identity = principal.Identity as ClaimsIdentity;
                    if (identity == null || string.IsNullOrEmpty(identity.AuthenticationType))
                    {
                        identity = new ClaimsIdentity(principal.Claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
                        principal = new ClaimsPrincipal(identity);
                    }

                    var ticket = new AuthenticationTicket(principal, Scheme.Name);
                    return Task.FromResult(AuthenticateResult.Success(ticket));
                }

                // Authorization présent mais invalide -> échec
                return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token (test handler)."));
            }
        }

        // 2) Fallback en-têtes de test
        var role = Request.Headers.TryGetValue("X-Test-Role", out var roleHeader) ? roleHeader.ToString() : null;
        var userId = Request.Headers.TryGetValue("X-Test-UserId", out var uidHeader) ? uidHeader.ToString() : null;

        if (!string.IsNullOrWhiteSpace(role))
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Role, role) };

            if (Guid.TryParse(userId, out var parsed))
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, parsed.ToString()));
                claims.Add(new Claim(ClaimTypes.Name, parsed.ToString()));
            }
            else
            {
                claims.Add(new Claim(ClaimTypes.Name, "test-user"));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        // 3) Aucun signal d'auth -> pas d'auth tentée (laissera [Authorize] provoquer un challenge 401)
        return Task.FromResult(AuthenticateResult.NoResult());
    }

    private static ClaimsPrincipal? TryValidate(string token, string issuer, string audience, string signingKey)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                RoleClaimType = ClaimTypes.Role
            };

            _ = handler.ValidateToken(token, parameters, out _);
            return handler.ValidateToken(token, parameters, out _);
        }
        catch
        {
            return null;
        }
    }
}
