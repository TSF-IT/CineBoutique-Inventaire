using System.Linq;
using System.Security.Cryptography;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Isopoh.Cryptography.Argon2;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Api.Auth;

public interface IShopUserAuthenticationService
{
    Task<ShopUserAuthenticationResult> AuthenticateAsync(Guid shopId, string login, string? secret, CancellationToken cancellationToken);
}

public sealed class ShopUserAuthenticationService : IShopUserAuthenticationService
{
    private static readonly string[] SupportedBcryptPrefixes = ["$2a$", "$2b$", "$2y$"];

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ShopUserAuthenticationService> _logger;

    public ShopUserAuthenticationService(
        IDbConnectionFactory connectionFactory,
        IHostEnvironment environment,
        ILogger<ShopUserAuthenticationService> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ShopUserAuthenticationResult> AuthenticateAsync(Guid shopId, string login, string? secret, CancellationToken cancellationToken)
    {
        if (shopId == Guid.Empty)
        {
            return ShopUserAuthenticationResult.Failure(ShopUserAuthenticationStatus.UserNotFound);
        }

        if (string.IsNullOrWhiteSpace(login))
        {
            return ShopUserAuthenticationResult.Failure(ShopUserAuthenticationStatus.UserNotFound);
        }

        var normalizedLogin = login.Trim();

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string sql = """
SELECT "Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Secret_Hash" AS SecretHash, "Disabled"
FROM "ShopUser"
WHERE "ShopId" = @ShopId AND LOWER("Login") = LOWER(@Login)
LIMIT 1;
""";

        var user = await connection
            .QuerySingleOrDefaultAsync<ShopUserRow>(sql, new { ShopId = shopId, Login = normalizedLogin })
            .ConfigureAwait(false);

        if (user is null)
        {
            return ShopUserAuthenticationResult.Failure(ShopUserAuthenticationStatus.UserNotFound);
        }

        if (user.Disabled)
        {
            return ShopUserAuthenticationResult.Failure(ShopUserAuthenticationStatus.UserDisabled);
        }

        var trimmedSecret = secret?.Trim();
        var secretHash = string.IsNullOrWhiteSpace(user.SecretHash) ? null : user.SecretHash;

        if (string.IsNullOrWhiteSpace(secretHash))
        {
            if (EnvironmentAllowsMissingSecret())
            {
                return ShopUserAuthenticationResult.Success(Map(user));
            }

            _logger.LogWarning(
                "Tentative de connexion pour {Login} sur la boutique {ShopId} alors que le secret est absent.",
                user.Login,
                user.ShopId);
            return ShopUserAuthenticationResult.Failure(ShopUserAuthenticationStatus.SecretNotConfigured);
        }

        if (string.IsNullOrEmpty(trimmedSecret))
        {
            return ShopUserAuthenticationResult.Failure(ShopUserAuthenticationStatus.SecretRequired);
        }

        var verification = VerifySecret(secretHash, trimmedSecret);
        if (!verification.Succeeded)
        {
            if (verification.Status == ShopUserAuthenticationStatus.UnsupportedSecret)
            {
                _logger.LogError(
                    "Algorithme de hash non pris en charge pour l'utilisateur {Login} (boutique {ShopId}).",
                    user.Login,
                    user.ShopId);
            }

            return ShopUserAuthenticationResult.Failure(verification.Status);
        }

        return ShopUserAuthenticationResult.Success(Map(user));
    }

    private static AuthenticatedShopUser Map(ShopUserRow row) =>
        new(row.Id, row.ShopId, row.Login, row.DisplayName, row.IsAdmin);

    private static SecretVerificationResult VerifySecret(string hash, string secret)
    {
        try
        {
            if (hash.StartsWith("$argon2id$", StringComparison.Ordinal))
            {
                return Argon2.Verify(hash, secret)
                    ? SecretVerificationResult.Success()
                    : SecretVerificationResult.Invalid();
            }

            if (SupportedBcryptPrefixes.Any(prefix => hash.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return BCrypt.Net.BCrypt.Verify(secret, hash)
                    ? SecretVerificationResult.Success()
                    : SecretVerificationResult.Invalid();
            }
        }
        catch (FormatException)
        {
            return SecretVerificationResult.Unsupported();
        }
        catch (CryptographicException)
        {
            return SecretVerificationResult.Unsupported();
        }
        catch (InvalidOperationException)
        {
            return SecretVerificationResult.Unsupported();
        }
        catch (ArgumentException)
        {
            return SecretVerificationResult.Unsupported();
        }

        return SecretVerificationResult.Unsupported();
    }

    private bool EnvironmentAllowsMissingSecret()
    {
        if (_environment.IsDevelopment())
        {
            return true;
        }

        return string.Equals(_environment.EnvironmentName, "CI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instancié via Dapper.")]
    private sealed record ShopUserRow(Guid Id, Guid ShopId, string Login, string DisplayName, bool IsAdmin, string? SecretHash, bool Disabled);

    private readonly record struct SecretVerificationResult(bool Succeeded, ShopUserAuthenticationStatus Status)
    {
        public static SecretVerificationResult Success() => new(true, ShopUserAuthenticationStatus.Success);
        public static SecretVerificationResult Invalid() => new(false, ShopUserAuthenticationStatus.InvalidSecret);
        public static SecretVerificationResult Unsupported() => new(false, ShopUserAuthenticationStatus.UnsupportedSecret);
    }
}

public enum ShopUserAuthenticationStatus
{
    Success,
    UserNotFound,
    SecretRequired,
    InvalidSecret,
    SecretNotConfigured,
    UnsupportedSecret,
    UserDisabled
}

public sealed record ShopUserAuthenticationResult
{
    private ShopUserAuthenticationResult(ShopUserAuthenticationStatus status, AuthenticatedShopUser? user)
    {
        Status = status;
        User = user;
    }

    public ShopUserAuthenticationStatus Status { get; }

    public AuthenticatedShopUser? User { get; }

    public bool Succeeded => Status == ShopUserAuthenticationStatus.Success && User is not null;

    public static ShopUserAuthenticationResult Success(AuthenticatedShopUser user)
        => new(ShopUserAuthenticationStatus.Success, user);

    public static ShopUserAuthenticationResult Failure(ShopUserAuthenticationStatus status)
        => new(status, null);
}
