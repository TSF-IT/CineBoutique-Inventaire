using System;
using System.Text.Json;
using CineBoutique.Inventory.Domain.Auditing;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CineBoutique.Inventory.Infrastructure.Auditing;

public sealed class DapperAuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DapperAuditLogger> _logger;
    private readonly DapperAuditLoggerOptions? _options;

    public DapperAuditLogger(
        IDbConnectionFactory connectionFactory,
        ILogger<DapperAuditLogger> logger,
        IOptions<DapperAuditLoggerOptions>? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value;
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            var schema = _options?.Schema;
            var configuredTableName = _options?.Table;
            var tableName = string.IsNullOrWhiteSpace(configuredTableName) ? "Audit" : configuredTableName;

            var qualifiedTable = string.IsNullOrWhiteSpace(schema)
                ? $"\"{tableName}\""
                : $"\"{schema}\".\"{tableName}\"";

            const string columns = "\"EntityName\", \"EntityId\", \"Event\", \"Actor\", \"OccurredAt\", \"Data\"";

            var sql = $@"
INSERT INTO {qualifiedTable} ({columns})
VALUES (@EntityName, @EntityId, @Event, @Actor, @OccurredAt, @Data);";

            var payloadJson = entry.Payload is null
                ? "{}"
                : JsonSerializer.Serialize(entry.Payload, SerializerOptions);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        EntityName = entry.EntityName ?? string.Empty,
                        EntityId = entry.EntityId ?? string.Empty,
                        Event = entry.EventType ?? string.Empty,
                        Actor = "system",
                        OccurredAt = entry.CreatedAtUtc,
                        Data = payloadJson
                    },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Audit logging failed for entity {Entity} with id {Id} and event {Event}",
                entry.EntityName,
                entry.EntityId,
                entry.EventType);
        }
    }
}

public sealed class DapperAuditLoggerOptions
{
    public string? Schema { get; init; }

    public string? Table { get; init; }
}
