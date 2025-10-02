using BCrypt.Net;

namespace CineBoutique.Inventory.Api.Auth;

public sealed class BcryptSecretHasher : ISecretHasher
{
    private const int WorkFactor = 12;

    public string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        return BCrypt.Net.BCrypt.EnhancedHashPassword(secret, HashType.SHA384, WorkFactor);
    }

    public bool Verify(string secret, string hash)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        return BCrypt.Net.BCrypt.EnhancedVerify(secret, hash, HashType.SHA384);
    }
}
