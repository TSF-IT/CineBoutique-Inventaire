using System.Linq;
using CineBoutique.Inventory.Domain.Admin;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CineBoutique.Inventory.Infrastructure.Admin;

public sealed class DapperAdminUserRepository : IAdminUserRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DapperAdminUserRepository> _logger;

    public DapperAdminUserRepository(IDbConnectionFactory connectionFactory, ILogger<DapperAdminUserRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AdminUserSearchResult> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "La page doit être supérieure ou égale à 1.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "La taille de page doit être supérieure ou égale à 1.");
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var offset = (page - 1) * pageSize;

        const string sql =
            """
            SELECT ""Id"", ""Email"", ""DisplayName"", ""CreatedAtUtc"", ""UpdatedAtUtc""
            FROM ""AdminUser""
            WHERE (@Query IS NULL)
                OR (""Email"" ILIKE '%' || @Query || '%' OR ""DisplayName"" ILIKE '%' || @Query || '%')
            ORDER BY ""DisplayName"" ASC, ""Email"" ASC
            OFFSET @Offset
            LIMIT @Limit;

            SELECT COUNT(*)
            FROM ""AdminUser""
            WHERE (@Query IS NULL)
                OR (""Email"" ILIKE '%' || @Query || '%' OR ""DisplayName"" ILIKE '%' || @Query || '%');
            """;

        using var gridReader = await connection.QueryMultipleAsync(
            new CommandDefinition(
                sql,
                new { Query = NormalizeQuery(query), Offset = offset, Limit = pageSize },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var rows = (await gridReader.ReadAsync<AdminUserRow>().ConfigureAwait(false)).ToList();
        var total = await gridReader.ReadFirstAsync<int>().ConfigureAwait(false);

        return new AdminUserSearchResult(rows.Select(Map).ToList(), total, page, pageSize);
    }

    public async Task<AdminUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string sql =
            """
            SELECT ""Id"", ""Email"", ""DisplayName"", ""CreatedAtUtc"", ""UpdatedAtUtc""
            FROM ""AdminUser""
            WHERE ""Id"" = @Id;
            """;

        var row = await connection.QueryFirstOrDefaultAsync<AdminUserRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null ? null : Map(row);
    }

    public async Task<AdminUser> CreateAsync(string email, string displayName, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string sql =
            """
            INSERT INTO ""AdminUser"" (""Id"", ""Email"", ""DisplayName"", ""CreatedAtUtc"")
            VALUES (@Id, @Email, @DisplayName, @CreatedAtUtc)
            RETURNING ""Id"", ""Email"", ""DisplayName"", ""CreatedAtUtc"", ""UpdatedAtUtc"";
            """;

        var id = Guid.NewGuid();

        try
        {
            var row = await connection.QuerySingleAsync<AdminUserRow>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Id = id,
                        Email = email,
                        DisplayName = displayName,
                        CreatedAtUtc = now
                    },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            return Map(row);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _logger.LogWarning(ex, "Impossible de créer l'utilisateur admin {Email} : doublon.", email);
            throw new InvalidOperationException($"L'adresse e-mail '{email}' est déjà utilisée.", ex);
        }
    }

    public async Task<AdminUser?> UpdateAsync(Guid id, string email, string displayName, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string sql =
            """
            UPDATE ""AdminUser""
            SET ""Email"" = @Email,
                ""DisplayName"" = @DisplayName,
                ""UpdatedAtUtc"" = @UpdatedAtUtc
            WHERE ""Id"" = @Id
            RETURNING ""Id"", ""Email"", ""DisplayName"", ""CreatedAtUtc"", ""UpdatedAtUtc"";
            """;

        try
        {
            var row = await connection.QueryFirstOrDefaultAsync<AdminUserRow>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Id = id,
                        Email = email,
                        DisplayName = displayName,
                        UpdatedAtUtc = now
                    },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            return row is null ? null : Map(row);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _logger.LogWarning(ex, "Impossible de modifier l'utilisateur admin {Email} : doublon.", email);
            throw new InvalidOperationException($"L'adresse e-mail '{email}' est déjà utilisée.", ex);
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string sql =
            """
            DELETE FROM ""AdminUser""
            WHERE ""Id"" = @Id;
            """;

        var affectedRows = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return affectedRows > 0;
    }

    private static string? NormalizeQuery(string? query)
    {
        return string.IsNullOrWhiteSpace(query) ? null : query.Trim();
    }

    private static AdminUser Map(AdminUserRow row)
    {
        return new AdminUser(row.Id, row.Email, row.DisplayName, row.CreatedAtUtc, row.UpdatedAtUtc);
    }

    private sealed record AdminUserRow(Guid Id, string Email, string DisplayName, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc);
}
