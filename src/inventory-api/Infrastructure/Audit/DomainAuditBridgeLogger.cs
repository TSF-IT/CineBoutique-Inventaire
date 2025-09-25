using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using ApiAuditLogger = CineBoutique.Inventory.Api.Infrastructure.Audit.IAuditLogger;

namespace CineBoutique.Inventory.Api.Infrastructure.Audit
{
    public sealed class DomainAuditBridgeLogger : CineBoutique.Inventory.Domain.Auditing.IAuditLogger
    {
        private readonly ApiAuditLogger _apiLogger;
        private readonly ILogger<DomainAuditBridgeLogger> _logger;

        public DomainAuditBridgeLogger(ApiAuditLogger apiLogger, ILogger<DomainAuditBridgeLogger> logger)
        {
            _apiLogger = apiLogger ?? throw new ArgumentNullException(nameof(apiLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task LogAsync(
            CineBoutique.Inventory.Domain.Auditing.AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            if (entry is null) throw new ArgumentNullException(nameof(entry));

            try
            {
                var payload = new
                {
                    entry.EntityName,
                    entry.EntityId,
                    entry.Event,
                    entry.Data
                };

                var message = JsonSerializer.Serialize(payload);
                var actor = string.IsNullOrWhiteSpace(entry.Actor) ? "system" : entry.Actor!;

                await _apiLogger.LogAsync(
                    message: message,
                    actor: actor,
                    category: entry.EntityName,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to bridge domain audit log for {Entity} {Id} {Event}.",
                    entry.EntityName, entry.EntityId, entry.Event);
            }
        }
    }
}
