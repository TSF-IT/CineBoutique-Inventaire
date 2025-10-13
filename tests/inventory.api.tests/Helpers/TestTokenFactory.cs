using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CineBoutique.Inventory.Api.Tests.Helpers;

public static class TestTokenFactory
{
    public const string SigningKey = "insecure-test-key-32bytes-minimum!!!!";
    public const string Issuer = "cineboutique-test";
    public const string Audience = "cineboutique-web";

    public static string Create(string role = "operator")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim(ClaimTypes.Role, role) };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string AdminToken() => Create("admin");
    public static string OperatorToken() => Create("operator");
    public static string ViewerToken() => Create("viewer");
}

