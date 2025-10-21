using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Npgsql;

namespace CineBoutique.Inventory.Infrastructure.Locks;

public sealed class PostgresImportLockService : IImportLockService
{
    private const string GlobalAcquireSql = "SELECT pg_try_advisory_lock(hashtext('product_import_global')::bigint);";
    private const string GlobalReleaseSql = "SELECT pg_advisory_unlock(hashtext('product_import_global')::bigint);";
    private const string ShopAcquireSql = "SELECT pg_try_advisory_lock(hashtext('product_import_shop_' || @ShopId::text)::bigint);";
    private const string ShopReleaseSql = "SELECT pg_advisory_unlock(hashtext('product_import_shop_' || @ShopId::text)::bigint);";

    private readonly IDbConnectionFactory _connectionFactory;

    public PostgresImportLockService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<IAsyncDisposable?> TryAcquireGlobalAsync(CancellationToken cancellationToken = default)
        => TryAcquireAsync(GlobalAcquireSql, GlobalReleaseSql, parameters: null, cancellationToken);

    public Task<IAsyncDisposable?> TryAcquireForShopAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        var parameters = new { ShopId = shopId };
        return TryAcquireAsync(ShopAcquireSql, ShopReleaseSql, parameters, cancellationToken);
    }

    private async Task<IAsyncDisposable?> TryAcquireAsync(
        string acquireSql,
        string releaseSql,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var acquired = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(acquireSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (!acquired)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        return new PostgresAdvisoryLockHandle(connection, releaseSql, parameters);
    }

    private sealed class PostgresAdvisoryLockHandle : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly string _releaseSql;
        private readonly object? _parameters;
        private bool _disposed;

        public PostgresAdvisoryLockHandle(NpgsqlConnection connection, string releaseSql, object? parameters)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _releaseSql = releaseSql ?? throw new ArgumentNullException(nameof(releaseSql));
            _parameters = parameters;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    await _connection.ExecuteScalarAsync<bool>(
                        new CommandDefinition(_releaseSql, _parameters, cancellationToken: CancellationToken.None))
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore release failures to avoid masking the original exception.
            }
            finally
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
