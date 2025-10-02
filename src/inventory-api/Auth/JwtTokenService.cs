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

    public TokenResult GenerateToken(ShopUserIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(_authenticationOptions.Secret))
        {
            throw new InvalidOperationException("Le secret JWT est absent de la configuration.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(_authenticationOptions.Secret);
        var signingCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

        var expires = DateTimeOffset.UtcNow.AddMinutes(_authenticationOptions.TokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, identity.UserId.ToString()),
            new(ClaimTypes.Name, identity.DisplayName),
            new(JwtRegisteredClaimNames.Name, identity.DisplayName),
            new("login", identity.Login),
            new("shop", identity.ShopName),
            new("shop_id", identity.ShopId.ToString()),
            new("is_admin", identity.IsAdmin ? "true" : "false")
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
