using System;
using System.Linq;
using CineBoutique.Inventory.Domain.Admin;
using CineBoutique.Inventory.Infrastructure.Admin;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CineBoutique.Inventory.Infrastructure.Admin
{
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
                SELECT id AS Id, email AS Email, display_name AS DisplayName, created_at AS CreatedAtUtc, updated_at AS UpdatedAtUtc
                FROM admin_users
                WHERE (@Query IS NULL)
                    OR (email ILIKE '%' || @Query || '%' OR display_name ILIKE '%' || @Query || '%')
                ORDER BY display_name ASC, email ASC
                OFFSET @Offset
                LIMIT @Limit;

                SELECT COUNT(*)
                FROM admin_users
                WHERE (@Query IS NULL)
                    OR (email ILIKE '%' || @Query || '%' OR display_name ILIKE '%' || @Query || '%');
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
                SELECT id AS Id, email AS Email, display_name AS DisplayName, created_at AS CreatedAtUtc, updated_at AS UpdatedAtUtc
                FROM admin_users
                WHERE id = @Id;
                """;

            var row = await connection.QueryFirstOrDefaultAsync<AdminUserRow>(
                new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return row is null ? null : Map(row);
        }

        public async Task<AdminUser> CreateAsync(string email, string displayName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email is required.", nameof(email));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Display name is required.", nameof(displayName));
            }

            var normalizedEmail = email.Trim();
            var normalizedDisplayName = displayName.Trim();
            var now = DateTimeOffset.UtcNow;

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            const string sql =
                """
                INSERT INTO admin_users (id, email, display_name, created_at, updated_at)
                VALUES (@Id, @Email, @DisplayName, @CreatedAt, @UpdatedAt)
                RETURNING id AS Id, email AS Email, display_name AS DisplayName, created_at AS CreatedAtUtc, updated_at AS UpdatedAtUtc;
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
                            Email = normalizedEmail,
                            DisplayName = normalizedDisplayName,
                            CreatedAt = now,
                            UpdatedAt = (DateTimeOffset?)null
                        },
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                return Map(row);
            }
            catch (PostgresException pgx) when (pgx.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                _logger.LogWarning(pgx, "Impossible de créer l'utilisateur admin {Email} : doublon.", normalizedEmail);
                throw new DuplicateUserException("An admin user with the same unique field already exists.", pgx);
            }
        }

        public async Task<AdminUser?> UpdateAsync(Guid id, string email, string displayName, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            const string sql =
                """
                UPDATE admin_users
                SET email = @Email,
                    display_name = @DisplayName,
                    updated_at = @UpdatedAt
                WHERE id = @Id
                RETURNING id AS Id, email AS Email, display_name AS DisplayName, created_at AS CreatedAtUtc, updated_at AS UpdatedAtUtc;
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
                            UpdatedAt = now
                        },
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                return row is null ? null : Map(row);
            }
            catch (PostgresException pgx) when (pgx.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                _logger.LogWarning(pgx, "Impossible de modifier l'utilisateur admin {Email} : doublon.", email);
                throw new DuplicateUserException("An admin user with the same unique field already exists.", pgx);
            }
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            const string sql =
                """
                DELETE FROM admin_users
                WHERE id = @Id;
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

}
