using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    private const string DefaultShopName = "CinéBoutique Saint-Denis";

    private const string InsertShopSql = @"
INSERT INTO ""Shop"" (""Name"", ""Kind"")
SELECT @Name, @Kind
WHERE NOT EXISTS (
    SELECT 1 FROM ""Shop"" WHERE LOWER(""Name"") = LOWER(@Name)
)
RETURNING ""Id"";";

    private const string SelectShopIdSql = @"SELECT ""Id"" FROM ""Shop"" WHERE LOWER(""Name"") = LOWER(@Name) LIMIT 1;";

    private const string RenameParisShopSql =
        "UPDATE \"Shop\" SET \"Name\"='CinéBoutique Saint-Denis' WHERE \"Name\"='CinéBoutique Paris';";

    private const string RenameBruxellesShopSql =
        "UPDATE \"Shop\" SET \"Name\"='CinéBoutique Belgique' WHERE \"Name\"='CinéBoutique Bruxelles';";

    private const string InsertLocationSql = @"INSERT INTO ""Location"" (""Code"", ""Label"", ""ShopId"")
SELECT @Code, @Label, @ShopId
WHERE NOT EXISTS (
    SELECT 1
    FROM ""Location""
    WHERE ""ShopId"" = @ShopId
      AND UPPER(""Code"") = UPPER(@Code));";

    private const string ShopHasCodeColumnSql = @"SELECT EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'Shop'
      AND column_name = 'Code');";

    private const string SelectShopsWithCodeSql = @"SELECT ""Id"", ""Code"", ""Name"" FROM ""Shop"" ORDER BY ""Name"";";

    private const string SelectShopsWithoutCodeSql = @"SELECT ""Id"", NULL::text AS ""Code"", ""Name"" FROM ""Shop"" ORDER BY ""Name"";";

    private const string UpsertShopUserSql = @"INSERT INTO ""ShopUser"" (""Id"", ""ShopId"", ""Login"", ""DisplayName"", ""IsAdmin"", ""Secret_Hash"", ""Disabled"")
VALUES (@Id, @ShopId, @Login, @DisplayName, @IsAdmin, '', FALSE)
ON CONFLICT (""ShopId"", ""DisplayName"")
DO UPDATE SET ""IsAdmin"" = EXCLUDED.""IsAdmin"",
              ""Disabled"" = FALSE,
              ""Login"" = EXCLUDED.""Login"";";

    private const string DisableNonTargetUsersSql = @"UPDATE ""ShopUser""
SET ""Disabled"" = TRUE,
    ""IsAdmin"" = FALSE
WHERE ""ShopId"" = @ShopId
  AND ""DisplayName"" <> ALL(@AllowedDisplayNames);";

    private const string PromoteShopAdministratorSql = @"UPDATE ""ShopUser""
SET ""IsAdmin"" = TRUE,
    ""Disabled"" = FALSE
WHERE ""ShopId"" = @ShopId
  AND ""DisplayName"" = @AdminDisplayName;";

    private const string DemoteOtherAdminsSql = @"UPDATE ""ShopUser""
SET ""IsAdmin"" = FALSE
WHERE ""ShopId"" = @ShopId
  AND ""DisplayName"" <> @AdminDisplayName;";

    private const string CountActiveAdminsSql = @"SELECT COUNT(*)
FROM ""ShopUser""
WHERE ""ShopId"" = @ShopId
  AND ""Disabled"" = FALSE
  AND ""IsAdmin"" = TRUE;";

    private static readonly IReadOnlyList<ShopSeed> ShopSeeds = BuildShopSeeds();
    private static readonly IReadOnlyList<LocationSeed> LocationSeeds = BuildLocationSeeds();

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
                var shopId = await GetShopIdAsync(connection, transaction, shopSeed, cancellationToken)
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

    private static async Task<int> EnsureShopsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ApplyShopRenamesAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        var insertedCount = 0;

        foreach (var shopSeed in ShopSeeds)
        {
            var insertedId = await connection.ExecuteScalarAsync<Guid?>(
                    new CommandDefinition(
                        InsertShopSql,
                        new
                        {
                            Name = shopSeed.Name,
                            Kind = shopSeed.Kind
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

    private static async Task ApplyShopRenamesAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
                new CommandDefinition(
                    RenameParisShopSql,
                    transaction: transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
                new CommandDefinition(
                    RenameBruxellesShopSql,
                    transaction: transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private static async Task<Guid> GetShopIdAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ShopSeed shopSeed,
        CancellationToken cancellationToken)
    {
        var existingId = await connection.ExecuteScalarAsync<Guid?>(
                new CommandDefinition(
                    SelectShopIdSql,
                    new
                    {
                        Name = shopSeed.Name
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
                        Name = shopSeed.Name,
                        Kind = shopSeed.Kind
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
        _ = shopIds; // Conserved for signature compatibility with location seeding logic.

        var hasCodeColumn = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    ShopHasCodeColumnSql,
                    transaction: transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var shops = (await connection.QueryAsync<ShopRow>(
                new CommandDefinition(
                    hasCodeColumn ? SelectShopsWithCodeSql : SelectShopsWithoutCodeSql,
                    transaction: transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false))
            .ToList();

        var affected = 0;

        foreach (var shop in shops)
        {
            affected += await EnsureUsersForShopAsync(connection, transaction, shop, cancellationToken)
                .ConfigureAwait(false);
        }

        return affected;
    }

    private async Task<int> EnsureUsersForShopAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ShopRow shop,
        CancellationToken cancellationToken)
    {
        var targetUsers = BuildTargetUsers(shop);
        var adminDisplayName = targetUsers.First(user => user.IsAdmin).DisplayName;

        var upserted = 0;

        foreach (var target in targetUsers)
        {
            upserted += await connection.ExecuteAsync(
                    new CommandDefinition(
                        UpsertShopUserSql,
                        new
                        {
                            Id = CreateStableGuid(shop.Id, target.DisplayName),
                            ShopId = shop.Id,
                            target.Login,
                            target.DisplayName,
                            target.IsAdmin
                        },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        var allowedDisplayNames = targetUsers
            .Select(user => user.DisplayName)
            .ToArray();

        var disabled = await connection.ExecuteAsync(
                new CommandDefinition(
                    DisableNonTargetUsersSql,
                    new
                    {
                        ShopId = shop.Id,
                        AllowedDisplayNames = allowedDisplayNames
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
                new CommandDefinition(
                    PromoteShopAdministratorSql,
                    new
                    {
                        ShopId = shop.Id,
                        AdminDisplayName = adminDisplayName
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
                new CommandDefinition(
                    DemoteOtherAdminsSql,
                    new
                    {
                        ShopId = shop.Id,
                        AdminDisplayName = adminDisplayName
                    },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var adminCount = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    CountActiveAdminsSql,
                    new { ShopId = shop.Id },
                    transaction,
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (adminCount != 1)
        {
            throw new InvalidOperationException(
                $"La boutique '{shop.Name}' doit posséder exactement un administrateur actif (actuel : {adminCount}).");
        }

        _logger.LogInformation(
            "Boutique {ShopName} ({ShopId}) : {TargetCount} utilisateurs cibles activés, {DisabledCount} comptes désactivés.",
            shop.Name,
            shop.Id,
            targetUsers.Count,
            disabled);

        return upserted + disabled;
    }

    private static IReadOnlyList<ShopSeed> BuildShopSeeds()
    {
        return new List<ShopSeed>
        {
            new(DefaultShopName, "boutique"),
            new("CinéBoutique Bordeaux", "boutique"),
            new("CinéBoutique Montpellier", "boutique"),
            new("CinéBoutique Marseille", "boutique"),
            new("CinéBoutique Belgique", "boutique"),
            new("Lumière Saint-Denis", "lumiere"),
            new("Lumière Marseille", "lumiere"),
            new("Lumière Montpellier", "lumiere"),
            new("Lumière Bordeaux", "lumiere"),
            new("Lumière Belgique", "lumiere")
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

    private static IReadOnlyList<TargetUser> BuildTargetUsers(ShopRow shop)
    {
        var cityLabel = ResolveCityLabel(shop);
        var displayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<TargetUser>();

        static void EnsureAdded(
            ShopRow targetShop,
            HashSet<string> knownDisplayNames,
            List<TargetUser> accumulator,
            TargetUser candidate)
        {
            if (!knownDisplayNames.Add(candidate.DisplayName))
            {
                throw new InvalidOperationException(
                    $"La boutique '{targetShop.Name}' tente de créer plusieurs comptes avec l'affichage '{candidate.DisplayName}'.");
            }

            accumulator.Add(candidate);
        }

        EnsureAdded(
            shop,
            displayNames,
            targets,
            new TargetUser("Administrateur", BuildLogin("Administrateur", isAdmin: true), true));

        if (IsParisShop(shop))
        {
            var baseDisplayName = $"Utilisateur {cityLabel}";
            EnsureAdded(
                shop,
                displayNames,
                targets,
                new TargetUser(baseDisplayName, BuildLogin(baseDisplayName, isAdmin: false), false));

            for (var index = 1; index <= 3; index++)
            {
                var numberedDisplayName = $"Utilisateur {cityLabel} {index}";
                EnsureAdded(
                    shop,
                    displayNames,
                    targets,
                    new TargetUser(numberedDisplayName, BuildLogin(numberedDisplayName, isAdmin: false), false));
            }
        }
        else
        {
            for (var index = 1; index <= 3; index++)
            {
                var displayName = $"Utilisateur {cityLabel} {index}";
                EnsureAdded(
                    shop,
                    displayNames,
                    targets,
                    new TargetUser(displayName, BuildLogin(displayName, isAdmin: false), false));
            }
        }

        return targets;
    }

    private static bool IsParisShop(ShopRow shop)
    {
        if (!string.IsNullOrWhiteSpace(shop.Code) && string.Equals(shop.Code, "PARIS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedName = RemoveDiacritics(shop.Name);
        return normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, "Paris", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveCityLabel(ShopRow shop)
    {
        if (IsParisShop(shop))
        {
            return "Paris";
        }

        var name = NormalizeSpaces(RemoveDiacritics(shop.Name));

        if (string.IsNullOrWhiteSpace(name))
        {
            return "Boutique";
        }

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> cityParts;

        if (parts.Length > 1 && string.Equals(parts[0], "CineBoutique", StringComparison.OrdinalIgnoreCase))
        {
            cityParts = parts.Skip(1);
        }
        else
        {
            cityParts = parts;
        }

        var city = NormalizeSpaces(string.Join(' ', cityParts));

        if (string.IsNullOrWhiteSpace(city))
        {
            city = "Boutique";
        }

        return string.Join(
            ' ',
            city.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(WordToTitleCase));
    }

    private static string BuildLogin(string displayName, bool isAdmin)
    {
        if (isAdmin)
        {
            return "administrateur";
        }

        var slug = ToSlug(displayName);
        return string.IsNullOrWhiteSpace(slug) ? "utilisateur" : slug;
    }

    [SuppressMessage("Globalization", "CA1308", Justification = "Slug/title normalization requires lowercase")]
    private static string WordToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var lower = value.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private static string NormalizeSpaces(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
            }
            else
            {
                builder.Append(character);
                previousWasSpace = false;
            }
        }

        return builder.ToString().Trim();
    }

    private static string RemoveDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [SuppressMessage("Globalization", "CA1308", Justification = "Slug/title normalization requires lowercase")]
    private static string ToSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = RemoveDiacritics(value).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        var previousWasDash = false;

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasDash = false;
            }
            else if (char.IsWhiteSpace(character) || character is '-' or '_' or '/')
            {
                if (!previousWasDash)
                {
                    builder.Append('-');
                    previousWasDash = true;
                }
            }
        }

        return builder.ToString().Trim('-');
    }

    private static Guid CreateStableGuid(Guid shopId, string displayName)
    {
        var shopBytes = shopId.ToByteArray();
        var displayNameBytes = Encoding.UTF8.GetBytes(displayName);
        var buffer = new byte[shopBytes.Length + displayNameBytes.Length];
        Buffer.BlockCopy(shopBytes, 0, buffer, 0, shopBytes.Length);
        Buffer.BlockCopy(displayNameBytes, 0, buffer, shopBytes.Length, displayNameBytes.Length);
        var hash = SHA256.HashData(buffer);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }

    private sealed record ShopSeed(string Name, string Kind);

    private sealed record LocationSeed(string ShopName, string Code, string Label);

    private sealed record ShopRow(Guid Id, string? Code, string Name);

    private sealed record TargetUser(string DisplayName, string Login, bool IsAdmin);
}
