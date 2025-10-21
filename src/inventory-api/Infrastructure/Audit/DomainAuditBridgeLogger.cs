using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using Microsoft.Extensions.Logging;
using Npgsql;
using CineBoutique.Inventory.Api.Infrastructure.Logging;
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
        ArgumentNullException.ThrowIfNull(apiAuditLogger);
        ArgumentNullException.ThrowIfNull(logger);

        _apiAuditLogger = apiAuditLogger;
        _logger = logger;
    }

    public async Task LogAsync(DomainAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

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
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            ApiLog.DomainAuditFallback(
                _logger,
                $"Failed to bridge domain audit event {entry.EventType} for {entry.EntityName} {entry.EntityId}: {ex.Message}");
            throw;
        }
        catch (CsvHelperException ex)
        {
            ApiLog.DomainAuditFallback(
                _logger,
                $"Failed to bridge domain audit event {entry.EventType} for {entry.EntityName} {entry.EntityId}: {ex.Message}");
        }
        catch (NpgsqlException ex)
        {
            ApiLog.DomainAuditFallback(
                _logger,
                $"Failed to bridge domain audit event {entry.EventType} for {entry.EntityName} {entry.EntityId}: {ex.Message}");
        }
        catch (Exception ex)
        {
            ApiLog.DomainAuditFallback(
                _logger,
                $"Failed to bridge domain audit event {entry.EventType} for {entry.EntityName} {entry.EntityId}: {ex.Message}");
            throw;
        }
    }
}
