using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Domain.Auditing;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CineBoutique.Inventory.Infrastructure.Auditing;

public sealed class DapperAuditLogger : IAuditLogger
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DapperAuditLogger> _logger;
    private readonly DapperAuditLoggerOptions? _options;

    public DapperAuditLogger(
        IDbConnectionFactory connectionFactory,
        IOptions<DapperAuditLoggerOptions>? options,
        ILogger<DapperAuditLogger> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value;
    }

    private string GetQualifiedTable()
    {
        var table = string.IsNullOrWhiteSpace(_options?.Table) ? "Audit" : _options!.Table!;
        var schema = _options?.Schema;

        return string.IsNullOrWhiteSpace(schema)
            ? $"\"{table}\""
            : $"\"{schema}\".\"{table}\"";
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var qualifiedTable = GetQualifiedTable();

            var sql = $@"
INSERT INTO {qualifiedTable}
    (""EntityName"", ""EntityId"", ""EventType"", ""Payload"", ""CreatedAtUtc"")
VALUES
    (@EntityName, @EntityId, @EventType, @Payload, @CreatedAtUtc);";

            var payloadJson = entry.Payload is null ? null : JsonSerializer.Serialize(entry.Payload);

            var parameters = new
            {
                entry.EntityName,
                entry.EntityId,
                entry.EventType,
                Payload = payloadJson,
                entry.CreatedAtUtc
            };

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(cmd).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Audit logging failed for entity {EntityName} with id {EntityId} and event {EventType}",
                entry.EntityName, entry.EntityId, entry.EventType);
        }
    }
}
