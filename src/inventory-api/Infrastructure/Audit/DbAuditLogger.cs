using System;
using System.Threading;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CineBoutique.Inventory.Api.Infrastructure.Audit;

public sealed class DbAuditLogger : IAuditLogger
{
    private readonly string _connectionString;
    private readonly ILogger<DbAuditLogger> _logger;

    public DbAuditLogger(IConfiguration configuration, ILogger<DbAuditLogger> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("La chaîne de connexion 'Default' est absente ou vide.");
        }

        _connectionString = connectionString;
    }

    public async Task LogAsync(string message, string? actor = null, string? category = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                _logger.LogWarning("Message d'audit vide, rien à enregistrer.");
                return;
            }

            var normalizedActor = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor.Trim();
            var trimmedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql =
                """
                INSERT INTO audit_logs (message, actor, category)
                VALUES (@Message, @Actor, @Category);
                """;

            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { Message = message, Actor = normalizedActor, Category = trimmedCategory },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Audit log failed for category {Category} by {Actor} with message {Message}",
                category ?? "(none)",
                string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor,
                string.IsNullOrWhiteSpace(message) ? "(empty)" : message);
        }
    }
}
