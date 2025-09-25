using System;
using System.Text.Json;
using CineBoutique.Inventory.Domain.Auditing;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Infrastructure.Auditing;

public sealed class DapperAuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DapperAuditLogger> _logger;

    public DapperAuditLogger(IDbConnectionFactory connectionFactory, ILogger<DapperAuditLogger> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            const string sql =
                """
                INSERT INTO ""Audit"" (""Id"", ""EntityName"", ""EntityId"", ""EventType"", ""Payload"", ""CreatedAtUtc"")
                VALUES (@Id, @EntityName, @EntityId, @EventType, CAST(@Payload AS jsonb), @CreatedAtUtc);
                """;

            var payloadJson = entry.Payload is null ? null : JsonSerializer.Serialize(entry.Payload, SerializerOptions);

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Id = Guid.NewGuid(),
                        entry.EntityName,
                        entry.EntityId,
                        entry.EventType,
                        Payload = payloadJson,
                        entry.CreatedAtUtc
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
