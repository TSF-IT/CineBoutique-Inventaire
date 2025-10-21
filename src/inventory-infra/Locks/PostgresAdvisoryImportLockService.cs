using System.Data;
using Dapper;

namespace CineBoutique.Inventory.Infrastructure.Locks;

public sealed class PostgresAdvisoryImportLockService(IDbConnection connection) : IImportLockService
{
    private sealed class Releaser : IAsyncDisposable
    {
        private readonly IDbConnection _conn;
        private readonly string _key;
        public Releaser(IDbConnection conn, string key) { _conn = conn; _key = key; }
        public async ValueTask DisposeAsync()
        {
            const string sql = "SELECT pg_advisory_unlock(hashtext(@k));";
            await _conn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { k = _key }));
        }
    }

    public async Task<IAsyncDisposable?> TryAcquireGlobalAsync(CancellationToken ct)
        => await TryAcquireAsync("product_import_global", ct);

    public async Task<IAsyncDisposable?> TryAcquireForShopAsync(Guid shopId, CancellationToken ct)
        => await TryAcquireAsync("product_import_shop_" + shopId.ToString("D"), ct);

    private async Task<IAsyncDisposable?> TryAcquireAsync(string key, CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open)
            await (connection as System.Data.Common.DbConnection)!.OpenAsync(ct).ConfigureAwait(false);

        const string sql = "SELECT pg_try_advisory_lock(hashtext(@k));";
        var ok = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new { k = key }, cancellationToken: ct))
                                  .ConfigureAwait(false);
        return ok ? new Releaser(connection, key) : null;
    }
}
