using Npgsql;

namespace CineBoutique.Inventory.Infrastructure.Database;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();

    Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);
}
