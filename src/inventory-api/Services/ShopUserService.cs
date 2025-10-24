using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Services.Exceptions;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Npgsql;

namespace CineBoutique.Inventory.Api.Services;

public sealed class ShopUserService : IShopUserService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ShopUserService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<ShopUserDto>> GetAsync(Guid shopId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await EnsureShopExistsAsync(connection, shopId, null, cancellationToken).ConfigureAwait(false);

        const string sql = """
        SELECT "Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Disabled"
        FROM "ShopUser"
        WHERE "ShopId" = @ShopId
          AND "Disabled" = FALSE
        ORDER BY "IsAdmin" DESC, "DisplayName";
        """;

        var users = await connection.QueryAsync<ShopUserDto>(
            new CommandDefinition(sql, new { ShopId = shopId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return users.ToList();
    }

    public async Task<ShopUserDto> CreateAsync(Guid shopId, CreateShopUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trimmedLogin = request.Login.Trim();
        var trimmedDisplayName = request.DisplayName.Trim();

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureShopExistsAsync(connection, shopId, transaction, cancellationToken).ConfigureAwait(false);

            const string sql = """
            INSERT INTO "ShopUser" ("ShopId", "Login", "DisplayName", "IsAdmin", "Secret_Hash", "Disabled")
            VALUES (@ShopId, @Login, @DisplayName, @IsAdmin, @SecretHash, FALSE)
            RETURNING "Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Disabled";
            """;

            var created = await connection.QuerySingleAsync<ShopUserDto>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            ShopId = shopId,
                            Login = trimmedLogin,
                            DisplayName = trimmedDisplayName,
                            request.IsAdmin,
                            SecretHash = string.Empty
                        },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return created;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ShopUserConflictException($"Impossible de créer cet utilisateur : l'identifiant « {trimmedLogin} » est déjà attribué dans cette boutique.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ShopUserDto> UpdateAsync(Guid shopId, UpdateShopUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trimmedLogin = request.Login.Trim();
        var trimmedDisplayName = request.DisplayName.Trim();

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureShopExistsAsync(connection, shopId, transaction, cancellationToken).ConfigureAwait(false);

            const string sql = """
            UPDATE "ShopUser"
            SET "Login" = @Login,
                "DisplayName" = @DisplayName,
                "IsAdmin" = @IsAdmin
            WHERE "Id" = @Id AND "ShopId" = @ShopId
            RETURNING "Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Disabled";
            """;

            var updated = await connection.QuerySingleOrDefaultAsync<ShopUserDto>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            request.Id,
                            ShopId = shopId,
                            Login = trimmedLogin,
                            DisplayName = trimmedDisplayName,
                            request.IsAdmin
                        },
                        transaction,
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (updated is null)
            {
                throw new ShopUserNotFoundException();
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return updated;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ShopUserConflictException($"Impossible de mettre à jour cet utilisateur : l'identifiant « {trimmedLogin} » est déjà attribué à un autre compte dans cette boutique.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ShopUserDto> SoftDeleteAsync(Guid shopId, DeleteShopUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureShopExistsAsync(connection, shopId, transaction, cancellationToken).ConfigureAwait(false);

            const string sql = """
            UPDATE "ShopUser"
            SET "Disabled" = TRUE
            WHERE "Id" = @Id AND "ShopId" = @ShopId
            RETURNING "Id", "ShopId", "Login", "DisplayName", "IsAdmin", "Disabled";
            """;

            var disabled = await connection.QuerySingleOrDefaultAsync<ShopUserDto>(
                    new CommandDefinition(sql, new { request.Id, ShopId = shopId }, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (disabled is null)
            {
                throw new ShopUserNotFoundException();
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return disabled;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureShopExistsAsync(
        NpgsqlConnection connection,
        Guid shopId,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM \"Shop\" WHERE \"Id\" = @ShopId);";

        var exists = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, new { ShopId = shopId }, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (!exists)
        {
            throw new ShopNotFoundException();
        }
    }
}
