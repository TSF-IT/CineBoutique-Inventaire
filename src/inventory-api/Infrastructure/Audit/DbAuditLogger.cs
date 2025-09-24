using System.Data;
using System.Data.Common;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Api.Infrastructure.Audit;

public sealed class DbAuditLogger : IAuditLogger
{
    private const string InsertSql = "INSERT INTO \"audit_logs\" (\"id\", \"ts\", \"user\", \"action\", \"details\") VALUES (@Id, @Timestamp, @User, @Action, @Details);";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DbAuditLogger> _logger;

    public DbAuditLogger(IDbConnectionFactory connectionFactory, ILogger<DbAuditLogger> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LogAsync(string? user, string message, string? action = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var trimmedUser = string.IsNullOrWhiteSpace(user) ? null : user.Trim();
        var trimmedAction = string.IsNullOrWhiteSpace(action) ? null : action.Trim();

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await EnsureConnectionOpenAsync(connection).ConfigureAwait(false);

            var parameters = new
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                User = trimmedUser,
                Action = trimmedAction,
                Details = message
            };

            await connection.ExecuteAsync(new CommandDefinition(InsertSql, parameters)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'enregistrement de l'audit: {Message}", message);
        }
    }

    private static async Task EnsureConnectionOpenAsync(IDbConnection connection)
    {
        switch (connection)
        {
            case DbConnection dbConnection when dbConnection.State != ConnectionState.Open:
                await dbConnection.OpenAsync().ConfigureAwait(false);
                break;
            case { State: ConnectionState.Closed }:
                connection.Open();
                break;
        }
    }
}
