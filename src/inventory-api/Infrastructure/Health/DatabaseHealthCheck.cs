using System.Data.Common;
using CineBoutique.Inventory.Infrastructure.Database;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace CineBoutique.Inventory.Api.Infrastructure.Health
{
    /// <summary>
    /// Vérifie que la connexion à la base est possible.
    /// </summary>
    public sealed class DatabaseHealthCheck(IDbConnectionFactory connectionFactory) : IHealthCheck
    {
        private readonly IDbConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            NpgsqlConnection? conn = null;
            try
            {
                conn = await _connectionFactory
                    .CreateOpenConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                _ = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return HealthCheckResult.Healthy("DB reachable");
            }
            catch (TimeoutException ex)
            {
                return HealthCheckResult.Unhealthy("DB unreachable", ex);
            }
            catch (DbException ex)
            {
                return HealthCheckResult.Unhealthy("DB unreachable", ex);
            }
            finally
            {
                if (conn is not null)
                    await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
