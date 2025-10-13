using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CineBoutique.Inventory.Api.Tests.Helpers;

internal static class JwtTestTokenFactory
{
    public const string Issuer = "https://auth.cineboutique.test";
    public const string Audience = "cineboutique.inventory.api";
    public const string SigningKey = "TEST_SIGNING_KEY_SHOULD_BE_SECURE_AND_AT_LEAST_32_CHARS_LONG";

    public static string CreateAdminToken(string subject = "admin-user", string? displayName = "Test Admin") =>
        CreateToken(subject, displayName, roles: ["admin"]);

    public static string CreateOperatorToken(string subject = "operator-user", string? displayName = "Test Operator") =>
        CreateToken(subject, displayName, roles: ["operator"]);

    public static string CreateViewerToken(string subject = "viewer-user", string? displayName = "Test Viewer") =>
        CreateToken(subject, displayName, roles: ["viewer"]);

    public static string CreateToken(
        string subject,
        string? displayName,
        TimeSpan? lifetime = null,
        IReadOnlyCollection<string>? roles = null)
    {
        var expiresIn = lifetime ?? TimeSpan.FromHours(1);
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, subject)
        };

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            claims.Add(new Claim(ClaimTypes.Name, displayName));
        }

        if (roles is not null)
        {
            foreach (var role in roles.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(expiresIn),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
