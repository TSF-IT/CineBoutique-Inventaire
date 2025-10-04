using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Infrastructure.Seeding;

public sealed class InventoryDataSeeder
{
    private const string DefaultShopName = "CinéBoutique Paris";

    private const string InsertShopSql = @"INSERT INTO ""Shop"" (""Name"")
VALUES (@Name)
ON CONFLICT DO NOTHING
RETURNING ""Id"";";

    private const string SelectShopIdSql = @"SELECT ""Id"" FROM ""Shop"" WHERE LOWER(""Name"") = LOWER(@Name) LIMIT 1;";

    private const string InsertLocationSql = @"INSERT INTO ""Location"" (""Code"", ""Label"", ""ShopId"")
SELECT @Code, @Label, @ShopId
WHERE NOT EXISTS (
    SELECT 1
    FROM ""Location""
    WHERE ""ShopId"" = @ShopId
      AND UPPER(""Code"") = UPPER(@Code));";

    private const string SelectParisUsersSql = @"SELECT ""Id"", ""DisplayName"", ""IsAdmin"", ""Disabled""
FROM ""ShopUser""
WHERE ""ShopId"" = @ShopId
ORDER BY ""Id"";";

    private const string PromoteParisAdminSql = @"UPDATE ""ShopUser""
SET ""IsAdmin"" = TRUE,
    ""Disabled"" = FALSE
WHERE ""Id"" = @UserId;";

    private const string DemoteParisUsersSql = @"UPDATE ""ShopUser""
SET ""IsAdmin"" = FALSE
WHERE ""ShopId"" = @ShopId
  AND ""Id"" <> @AdminId;";

    private const string UpsertNonParisUserSql = @"INSERT INTO ""ShopUser"" (""Id"", ""ShopId"", ""Login"", ""DisplayName"", ""IsAdmin"", ""Secret_Hash"", ""Disabled"")
VALUES (@Id, @ShopId, @Login, @DisplayName, @IsAdmin, @SecretHash, FALSE)
ON CONFLICT (""ShopId"", ""DisplayName"")
DO UPDATE SET ""IsAdmin"" = EXCLUDED.""IsAdmin"",
              ""Disabled"" = EXCLUDED.""Disabled"",
              ""Login"" = EXCLUDED.""Login"";";

    private const string DisableNonParisSurplusSql = @"UPDATE ""ShopUser""
SET ""Disabled"" = TRUE,
    ""IsAdmin"" = FALSE
WHERE ""ShopId"" = @ShopId
  AND NOT EXISTS (
        SELECT 1
        FROM UNNEST(@AllowedDisplayNames) AS allowed(name)
        WHERE LOWER(allowed.name) = LOWER(""DisplayName"")
    );";

    private const string CountActiveAdminsSql = @"SELECT COUNT(*)
FROM ""ShopUser""
WHERE ""ShopId"" = @ShopId
  AND ""Disabled"" = FALSE
  AND ""IsAdmin"" = TRUE;";

    private const string CountShopUsersSql = @"SELECT COUNT(*)
FROM ""ShopUser""
WHERE ""ShopId"" = @ShopId;";

    private static readonly IReadOnlyList<ShopSeed> ShopSeeds = BuildShopSeeds();
    private static readonly IReadOnlyList<LocationSeed> LocationSeeds = BuildLocationSeeds();

    private static readonly IReadOnlyList<NonParisUserSeed> NonParisSeeds = new List<NonParisUserSeed>
    {
        new("Administrateur", "administrateur", true),
        new("Utilisateur 1", "utilisateur1", false),
        new("Utilisateur 2", "utilisateur2", false),
        new("Utilisateur 3", "utilisateur3", false)
    };

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<InventoryDataSeeder> _logger;

    public InventoryDataSeeder(IDbConnectionFactory connectionFactory, ILogger<InventoryDataSeeder> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var insertedShopCount = await EnsureShopsAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);

            var shopIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            foreach (var shopSeed in ShopSeeds)
            {
                var shopId = await GetShopIdAsync(connection, transaction, shopSeed.Name, cancellationToken)
                    .ConfigureAwait(false);
                shopIds[shopSeed.Name] = shopId;
            }

            var insertedLocationCount = 0;

            foreach (var seed in LocationSeeds)
            {
                if (!shopIds.TryGetValue(seed.ShopName, out var shopId))
                {
                    continue;
                }

                var code = seed.Code.ToUpperInvariant();
                var label = seed.Label ?? $"Zone {code}";

                var affectedRows = await connection.ExecuteAsync(
                        new CommandDefinition(
                            InsertLocationSql,
                            new
                            {
                                Code = code,
                                Label = label,
                                ShopId = shopId
                            },
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                if (affectedRows > 0)
                {
                    insertedLocationCount += affectedRows;
                }
            }

            var insertedUserCount = await EnsureShopUsersAsync(
                    connection,
                    transaction,
                    shopIds,
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Seed terminé. {InsertedShopCount} magasins, {InsertedLocationCount} zones et {InsertedUserCount} comptes utilisateurs créés (idempotent).",
                insertedShopCount,
                insertedLocationCount,
                insertedUserCount);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Échec de l'initialisation des magasins/zones d'inventaire.");
            throw;
        }
    }

    private async Task<int> EnsureShopsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var insertedCount = 0;

        foreach (var shopSeed in ShopSeeds)
        {
            var insertedId = await connection.ExecuteScalarAsync<Guid?>(
                    new CommandDefinition(
                        InsertShopSql,
                        new
                        {
                            shopSeed.Name
                        },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (insertedId.HasValue)
            {
                insertedCount++;
            }
        }

        return insertedCount;
    }

    private static async Task<Guid> GetShopIdAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        var existingId = await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(
                    SelectShopIdSql,
                    new
                    {
                        Name = name
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        var insertedId = await connection.ExecuteScalarAsync<Guid>(
                new CommandDefinition(
                    InsertShopSql,
                    new
                    {
                        Name = name
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return insertedId;
    }

    private async Task<int> EnsureShopUsersAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyDictionary<string, Guid> shopIds,
        CancellationToken cancellationToken)
    {
        var affected = 0;

        if (shopIds.TryGetValue(DefaultShopName, out var parisShopId))
        {
            affected += await EnsureParisUsersAsync(connection, transaction, parisShopId, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var (shopName, shopId) in shopIds)
        {
            if (string.Equals(shopName, DefaultShopName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            affected += await EnsureNonParisUsersAsync(connection, transaction, shopId, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var (shopName, shopId) in shopIds)
        {
            await EnsureSingleActiveAdminAsync(connection, transaction, shopName, shopId, cancellationToken)
                .ConfigureAwait(false);
        }

        return affected;
    }

    private async Task<int> EnsureParisUsersAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var users = (await connection.QueryAsync<ParisUserRow>(
                new CommandDefinition(
                    SelectParisUsersSql,
                    new { ShopId = shopId },
                    transaction,
                    cancellationToken: cancellationToken)))
            .ToList();

        if (users.Count == 0)
        {
            return 0;
        }

        var adminUser = users.FirstOrDefault(
                           user => string.Equals(user.DisplayName, "Arnaud", StringComparison.OrdinalIgnoreCase))
                       ?? users.OrderBy(user => user.Id).First();

        var affected = 0;

        affected += await connection.ExecuteAsync(
                new CommandDefinition(
                    PromoteParisAdminSql,
                    new { UserId = adminUser.Id },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        affected += await connection.ExecuteAsync(
                new CommandDefinition(
                    DemoteParisUsersSql,
                    new
                    {
                        ShopId = shopId,
                        AdminId = adminUser.Id
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return affected;
    }

    private async Task<int> EnsureNonParisUsersAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var affected = 0;

        foreach (var seed in NonParisSeeds)
        {
            affected += await connection.ExecuteAsync(
                    new CommandDefinition(
                        UpsertNonParisUserSql,
                        new
                        {
                            Id = CreateStableGuid(shopId, seed.DisplayName),
                            ShopId = shopId,
                            seed.Login,
                            seed.DisplayName,
                            seed.IsAdmin,
                            SecretHash = string.Empty
                        },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        var allowedDisplayNames = NonParisSeeds
            .Select(seed => seed.DisplayName)
            .ToArray();

        affected += await connection.ExecuteAsync(
                new CommandDefinition(
                    DisableNonParisSurplusSql,
                    new
                    {
                        ShopId = shopId,
                        AllowedDisplayNames = allowedDisplayNames
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return affected;
    }

    private async Task EnsureSingleActiveAdminAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string shopName,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var totalUsers = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    CountShopUsersSql,
                    new { ShopId = shopId },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (totalUsers == 0)
        {
            return;
        }

        var adminCount = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    CountActiveAdminsSql,
                    new { ShopId = shopId },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (adminCount != 1)
        {
            throw new InvalidOperationException(
                $"La boutique '{shopName}' doit posséder exactement un administrateur actif (actuel : {adminCount}).");
        }
    }

    private static IReadOnlyList<ShopSeed> BuildShopSeeds()
    {
        return new List<ShopSeed>
        {
            new(DefaultShopName),
            new("CinéBoutique Bordeaux"),
            new("CinéBoutique Montpellier"),
            new("CinéBoutique Marseille"),
            new("CinéBoutique Bruxelles")
        };
    }

    private static IReadOnlyList<LocationSeed> BuildLocationSeeds()
    {
        var seeds = new List<LocationSeed>(39 + (ShopSeeds.Count - 1) * 5);

        for (var index = 1; index <= 20; index++)
        {
            var code = $"B{index}";
            seeds.Add(new LocationSeed(DefaultShopName, code, $"Zone {code}"));
        }

        for (var index = 1; index <= 19; index++)
        {
            var code = $"S{index}";
            seeds.Add(new LocationSeed(DefaultShopName, code, $"Zone {code}"));
        }

        var demoCodes = new[] { "A", "B", "C", "D", "E" };

        foreach (var shopSeed in ShopSeeds.Where(seed => !string.Equals(seed.Name, DefaultShopName, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var code in demoCodes)
            {
                var normalizedCode = code.ToUpperInvariant();
                seeds.Add(new LocationSeed(shopSeed.Name, normalizedCode, $"Zone {normalizedCode}"));
            }
        }

        return seeds;
    }

    private static Guid CreateStableGuid(Guid shopId, string displayName)
    {
        using var md5 = MD5.Create();
        var shopBytes = shopId.ToByteArray();
        var displayNameBytes = Encoding.UTF8.GetBytes(displayName);
        var buffer = new byte[shopBytes.Length + displayNameBytes.Length];
        Buffer.BlockCopy(shopBytes, 0, buffer, 0, shopBytes.Length);
        Buffer.BlockCopy(displayNameBytes, 0, buffer, shopBytes.Length, displayNameBytes.Length);
        var hash = md5.ComputeHash(buffer);
        return new Guid(hash);
    }

    private sealed record ShopSeed(string Name);

    private sealed record LocationSeed(string ShopName, string Code, string Label);

    private sealed record ParisUserRow(Guid Id, string DisplayName, bool IsAdmin, bool Disabled);

    private sealed record NonParisUserSeed(string DisplayName, string Login, bool IsAdmin);
}
