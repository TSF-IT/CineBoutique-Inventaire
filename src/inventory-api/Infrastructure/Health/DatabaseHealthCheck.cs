using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CineBoutique.Inventory.Api.Infrastructure.Health
{
    /// <summary>
    /// Vérifie que la connexion à la base est possible.
    /// </summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public DatabaseHealthCheck(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await using var conn = await _connectionFactory
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
        }
    }
}
