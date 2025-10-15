using System;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CineBoutique.Inventory.Infrastructure.Database;

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
                await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return HealthCheckResult.Healthy("DB reachable");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("DB unreachable", ex);
            }
        }
    }
}
