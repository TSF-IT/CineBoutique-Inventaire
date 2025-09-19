using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CineBoutique.Inventory.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CineBoutique.Inventory.Api.Auth;

public sealed class JwtTokenService : ITokenService
{
    private readonly AuthenticationOptions _authenticationOptions;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public JwtTokenService(IOptions<AuthenticationOptions> authenticationOptions)
    {
        ArgumentNullException.ThrowIfNull(authenticationOptions);
        _authenticationOptions = authenticationOptions.Value;
    }

    public TokenResult GenerateToken(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        if (string.IsNullOrWhiteSpace(_authenticationOptions.Secret))
        {
            throw new InvalidOperationException("Le secret JWT est absent de la configuration.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(_authenticationOptions.Secret);
        var signingCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

        var expires = DateTimeOffset.UtcNow.AddMinutes(_authenticationOptions.TokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName)
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires.UtcDateTime,
            Issuer = _authenticationOptions.Issuer,
            Audience = _authenticationOptions.Audience,
            SigningCredentials = signingCredentials
        };

        var securityToken = _tokenHandler.CreateToken(descriptor);
        var accessToken = _tokenHandler.WriteToken(securityToken);

        return new TokenResult(accessToken, expires);
    }
}
