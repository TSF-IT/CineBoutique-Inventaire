using System;
using System.Threading;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using CineBoutique.Inventory.Api.Endpoints;
using CineBoutique.Inventory.Api.Infrastructure.Logging;

namespace CineBoutique.Inventory.Api.Infrastructure.Audit;

public sealed class DbAuditLogger : IAuditLogger
{
    private readonly string _connectionString;
    private readonly ILogger<DbAuditLogger> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DbAuditLogger(IConfiguration configuration, ILogger<DbAuditLogger> logger, IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

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

            var httpContext = _httpContextAccessor.HttpContext;
            ArgumentNullException.ThrowIfNull(httpContext);

            var operatorContext = EndpointUtilities.GetOperatorContext(httpContext!);
            var composedActor = EndpointUtilities.ComposeAuditActor(actor, operatorContext);
            var normalizedActor = string.IsNullOrWhiteSpace(composedActor) ? "anonymous" : composedActor!;
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
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NpgsqlException ex)
        {
            ApiLog.DbAuditWriteFailed(_logger, ex);
        }
        catch (Exception ex)
        {
            ApiLog.DbAuditWriteFailed(_logger, ex);
            throw;
        }
    }
}
