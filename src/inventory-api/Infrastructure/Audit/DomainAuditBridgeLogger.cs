using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using DomainAuditEntry = CineBoutique.Inventory.Domain.Auditing.AuditEntry;
using IDomainAuditLogger = CineBoutique.Inventory.Domain.Auditing.IAuditLogger;
using IApiAuditLogger = CineBoutique.Inventory.Api.Infrastructure.Audit.IAuditLogger;

namespace CineBoutique.Inventory.Api.Infrastructure.Audit;

/// <summary>
/// Pont facultatif qui convertit l'AuditEntry du domaine en une ligne texte pour le logger API.
/// Utile uniquement si tu veux aussi écrire dans audit_logs en plus de la table Audit.
/// </summary>
public sealed class DomainAuditBridgeLogger : IDomainAuditLogger
{
    private readonly IApiAuditLogger _apiAuditLogger;
    private readonly ILogger<DomainAuditBridgeLogger> _logger;

    public DomainAuditBridgeLogger(IApiAuditLogger apiAuditLogger, ILogger<DomainAuditBridgeLogger> logger)
    {
        _apiAuditLogger = apiAuditLogger ?? throw new ArgumentNullException(nameof(apiAuditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LogAsync(DomainAuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        var category = entry.EntityName;
        var message = $"{entry.EventType} {entry.EntityName} {entry.EntityId}";
        var payload = entry.Payload is null ? null : JsonSerializer.Serialize(entry.Payload);

        try
        {
            // On n'a pas d'Actor dans AuditEntry (nouveau modèle) -> null.
            await _apiAuditLogger.LogAsync(
                payload is null ? message : $"{message} :: {payload}",
                actor: null,
                category: category,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bridge domain audit event {EventType} for {EntityName} {EntityId}",
                entry.EventType, entry.EntityName, entry.EntityId);
        }
    }
}
