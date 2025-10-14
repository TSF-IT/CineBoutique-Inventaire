using System.Data;
using System.Security.Cryptography;
using System.Text;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Infrastructure.Seeding;

public sealed class InventoryE2ESeeder
{
    private const string ShopCode = "PARIS";
    private const string ShopName = "CinéBoutique Paris";
    private const string ShopUserDisplayName = "Utilisateur Paris";
    private const string ShopUserLogin = "utilisateur.paris";
    private const string LocationCode = "Z1";
    private const string LocationLabel = "Zone 1";

    private const string UpsertShopSql = """
INSERT INTO "Shop" ("Id", "Code", "Name")
VALUES (@Id, @Code, @Name)
ON CONFLICT ("Code") DO UPDATE
    SET "Name" = EXCLUDED."Name"
RETURNING "Id";
""";

    private const string UpsertShopUserSql = """
INSERT INTO "ShopUser" ("Id", "ShopId", "DisplayName", "Login", "IsAdmin", "Disabled", "Secret_Hash")
VALUES (@Id, @ShopId, @DisplayName, @Login, @IsAdmin, FALSE, '')
ON CONFLICT ("ShopId", "DisplayName") DO UPDATE
    SET "Login" = EXCLUDED."Login",
        "IsAdmin" = EXCLUDED."IsAdmin",
        "Disabled" = FALSE;
""";

    private const string UpsertLocationSql = """
INSERT INTO "Location" ("Id", "ShopId", "Code", "Label")
VALUES (@Id, @ShopId, @Code, @Label)
ON CONFLICT ("ShopId", "Code") DO UPDATE
    SET "Label" = EXCLUDED."Label";
""";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<InventoryE2ESeeder> _logger;

    public InventoryE2ESeeder(IDbConnectionFactory connectionFactory, ILogger<InventoryE2ESeeder> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            var shopId = await EnsureShopAsync(connection, transaction, ct).ConfigureAwait(false);
            var shopUserId = await EnsureShopUserAsync(connection, transaction, shopId, ct).ConfigureAwait(false);
            var locationId = await EnsureLocationAsync(connection, transaction, shopId, ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "E2E seed upserted shop {ShopCode} ({ShopId}), user {ShopUserLogin} ({ShopUserId}) and location {LocationCode} ({LocationId}).",
                ShopCode,
                shopId,
                ShopUserLogin,
                shopUserId,
                LocationCode,
                locationId);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private static Guid StableGuid(string key)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static Guid BuildShopId() => StableGuid($"shop:{ShopCode}");

    private static Guid BuildShopUserId(Guid shopId) => StableGuid($"shopuser:{shopId}:{ShopUserLogin}");

    private static Guid BuildLocationId(Guid shopId) => StableGuid($"location:{shopId}:{LocationCode}");

    private async Task<Guid> EnsureShopAsync(IDbConnection connection, IDbTransaction transaction, CancellationToken ct)
    {
        var parameters = new
        {
            Id = BuildShopId(),
            Code = ShopCode,
            Name = ShopName
        };

        return await connection.ExecuteScalarAsync<Guid>(
                new CommandDefinition(UpsertShopSql, parameters, transaction, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    private async Task<Guid> EnsureShopUserAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid shopId,
        CancellationToken ct)
    {
        var parameters = new
        {
            Id = BuildShopUserId(shopId),
            ShopId = shopId,
            DisplayName = ShopUserDisplayName,
            Login = ShopUserLogin,
            IsAdmin = false
        };

        await connection.ExecuteAsync(
                new CommandDefinition(UpsertShopUserSql, parameters, transaction, cancellationToken: ct))
            .ConfigureAwait(false);

        return parameters.Id;
    }

    private async Task<Guid> EnsureLocationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid shopId,
        CancellationToken ct)
    {
        var parameters = new
        {
            Id = BuildLocationId(shopId),
            ShopId = shopId,
            Code = LocationCode,
            Label = LocationLabel
        };

        await connection.ExecuteAsync(
                new CommandDefinition(UpsertLocationSql, parameters, transaction, cancellationToken: ct))
            .ConfigureAwait(false);

        return parameters.Id;
    }
}
