using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Infrastructure.Seeding;

public sealed class InventoryDataSeeder
{
    private const string DefaultShopName = "CinéBoutique Paris";

    private static readonly ImmutableArray<string> SeedShopNames =
    [
        "CinéBoutique Paris",
        "CinéBoutique Bordeaux",
        "CinéBoutique Montpellier",
        "CinéBoutique Marseille",
        "CinéBoutique Bruxelles"
    ];

    private static readonly ImmutableArray<string> DemoLocationCodes =
    [
        "A",
        "B",
        "C",
        "D",
        "E"
    ];

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
            var shops = await EnsureShopsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var parisShopId = shops[DefaultShopName];

            await BackfillLocationsAsync(connection, transaction, parisShopId, cancellationToken).ConfigureAwait(false);
            await EnsureDemoLocationsAsync(connection, transaction, shops, cancellationToken).ConfigureAwait(false);
            await EnsureShopUsersAsync(connection, transaction, shops, cancellationToken).ConfigureAwait(false);
            await BackfillCountingRunOwnersAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Seed terminé pour {ShopCount} boutiques.", shops.Count);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Échec de l'initialisation des données d'inventaire.");
            throw;
        }
    }

    private static async Task<Dictionary<string, Guid>> EnsureShopsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        const string selectShopSql = """
SELECT "Id" FROM "Shop" WHERE lower("Name") = lower(@Name) LIMIT 1;
""";
        const string insertShopSql = """
INSERT INTO "Shop" ("Name") VALUES (@Name)
ON CONFLICT DO NOTHING
RETURNING "Id";
""";

        foreach (var name in SeedShopNames)
        {
            var existing = await connection.ExecuteScalarAsync<Guid?>(
                    new CommandDefinition(
                        selectShopSql,
                        new { Name = name },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (existing is Guid id)
            {
                result[name] = id;
                continue;
            }

            var inserted = await connection.ExecuteScalarAsync<Guid?>(
                    new CommandDefinition(
                        insertShopSql,
                        new { Name = name },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (inserted is Guid newId)
            {
                result[name] = newId;
                continue;
            }

            var fallback = await connection.ExecuteScalarAsync<Guid>(
                    new CommandDefinition(
                        selectShopSql,
                        new { Name = name },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            result[name] = fallback;
        }

        return result;
    }

    private static Task BackfillLocationsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid parisShopId,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(
            new CommandDefinition(
                """
UPDATE "Location" SET "ShopId" = @ShopId WHERE "ShopId" IS NULL;
""",
                new { ShopId = parisShopId },
                transaction,
                cancellationToken: cancellationToken));

    private static async Task EnsureDemoLocationsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyDictionary<string, Guid> shops,
        CancellationToken cancellationToken)
    {
        const string insertLocationSql = """
INSERT INTO "Location" ("ShopId", "Code", "Label")
VALUES (@ShopId, @Code, @Label)
ON CONFLICT DO NOTHING;
""";
        const string updateLocationSql = """
UPDATE "Location"
SET "Label" = @Label
WHERE "ShopId" = @ShopId AND upper("Code") = upper(@Code);
""";

        foreach (var (name, id) in shops)
        {
            if (string.Equals(name, DefaultShopName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var code in DemoLocationCodes)
            {
                var normalizedCode = code.ToUpperInvariant();
                var label = $"Zone {normalizedCode}";

                var parameters = new
                {
                    ShopId = id,
                    Code = normalizedCode,
                    Label = label
                };

                await connection.ExecuteAsync(
                        new CommandDefinition(
                            insertLocationSql,
                            parameters,
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                await connection.ExecuteAsync(
                        new CommandDefinition(
                            updateLocationSql,
                            parameters,
                            transaction,
                            cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task EnsureShopUsersAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyDictionary<string, Guid> shops,
        CancellationToken cancellationToken)
    {
        foreach (var (name, id) in shops)
        {
            await UpsertUserAsync(connection, transaction, id, "administrateur", "Administrateur", true, cancellationToken)
                .ConfigureAwait(false);

            var additionalUsers = string.Equals(name, DefaultShopName, StringComparison.Ordinal) ? 5 : 4;

            for (var index = 1; index <= additionalUsers; index++)
            {
                var login = $"utilisateur{index}";
                var displayName = $"Utilisateur {index}";

                await UpsertUserAsync(connection, transaction, id, login, displayName, false, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task UpsertUserAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid shopId,
        string login,
        string displayName,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var normalizedLogin = login.Trim();
        var normalizedDisplayName = displayName.Trim();
        var parameters = new
        {
            ShopId = shopId,
            Login = normalizedLogin,
            LoginLower = normalizedLogin.ToLowerInvariant(),
            DisplayName = normalizedDisplayName,
            IsAdmin = isAdmin
        };

        var insertedId = await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(
                    InsertShopUserSql,
                    parameters,
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (insertedId is null)
        {
            await connection.ExecuteAsync(
                    new CommandDefinition(
                        UpdateShopUserSql,
                        parameters,
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }
    }

    private const string InsertShopUserSql = """
INSERT INTO "ShopUser" ("ShopId", "Login", "DisplayName", "IsAdmin", "Secret_Hash", "Disabled")
VALUES (@ShopId, @Login, @DisplayName, @IsAdmin, NULL, FALSE)
ON CONFLICT DO NOTHING
RETURNING "Id";
""";

    private const string UpdateShopUserSql = """
UPDATE "ShopUser"
SET "DisplayName" = @DisplayName,
    "IsAdmin" = @IsAdmin,
    "Disabled" = FALSE
WHERE "ShopId" = @ShopId AND lower("Login") = @LoginLower;
""";

    private static Task BackfillCountingRunOwnersAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(
            new CommandDefinition(
                """
UPDATE "CountingRun" cr
SET "OwnerUserId" = su."Id"
FROM "Location" l
JOIN "ShopUser" su ON su."ShopId" = l."ShopId" AND su."DisplayName" = btrim(cr."OperatorDisplayName") AND su."Disabled" = FALSE
WHERE l."Id" = cr."LocationId"
  AND cr."OwnerUserId" IS NULL
  AND cr."OperatorDisplayName" IS NOT NULL
  AND btrim(cr."OperatorDisplayName") <> '';
""",
                transaction: transaction,
                cancellationToken: cancellationToken));
}
