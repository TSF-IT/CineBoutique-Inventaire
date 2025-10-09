using System;
using System.Threading.Tasks;
using Npgsql;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class TestDataSeeder
{
    private readonly NpgsqlDataSource _dataSource;

    public TestDataSeeder(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<Guid> CreateShopAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var id = Guid.NewGuid();

        const string sql = "INSERT INTO \"Shop\" (\"Id\", \"Name\") VALUES (@id, @name);";

        var connection = _dataSource.CreateConnection();
        await connection.OpenAsync().ConfigureAwait(false);
        try
        {
            using var command = new NpgsqlCommand(sql, connection)
            {
                Parameters =
                {
                    new("id", id),
                    new("name", name)
                }
            };

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        return id;
    }

    public async Task<Guid> CreateLocationAsync(Guid shopId, string code, string label)
    {
        if (shopId == Guid.Empty)
        {
            throw new ArgumentException("Shop identifier is required.", nameof(shopId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var id = Guid.NewGuid();

        const string sql = "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\", \"ShopId\") VALUES (@id, @code, @label, @shopId);";

        var connection = _dataSource.CreateConnection();
        await connection.OpenAsync().ConfigureAwait(false);
        try
        {
            using var command = new NpgsqlCommand(sql, connection)
            {
                Parameters =
                {
                    new("id", id),
                    new("code", code),
                    new("label", label),
                    new("shopId", shopId)
                }
            };

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        return id;
    }

    public async Task<Guid> CreateShopUserAsync(Guid shopId, string login, string displayName, bool isAdmin = false)
    {
        if (shopId == Guid.Empty)
        {
            throw new ArgumentException("Shop identifier is required.", nameof(shopId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var id = Guid.NewGuid();

        const string sql =
            "INSERT INTO \"ShopUser\" (\"Id\", \"ShopId\", \"Login\", \"DisplayName\", \"IsAdmin\", \"Secret_Hash\", \"Disabled\") " +
            "VALUES (@id, @shopId, @login, @displayName, @isAdmin, @secret, FALSE);";

        var connection = _dataSource.CreateConnection();
        await connection.OpenAsync().ConfigureAwait(false);
        try
        {
            using var command = new NpgsqlCommand(sql, connection)
            {
                Parameters =
                {
                    new("id", id),
                    new("shopId", shopId),
                    new("login", login),
                    new("displayName", displayName),
                    new("isAdmin", isAdmin),
                    new("secret", "test-secret")
                }
            };

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        return id;
    }

    public async Task<Guid> CreateProductAsync(string sku, string name, string? ean = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var id = Guid.NewGuid();

        const string sql =
            "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@id, @sku, @name, @ean, @createdAt);";

        var connection = _dataSource.CreateConnection();
        await connection.OpenAsync().ConfigureAwait(false);
        try
        {
            using var command = new NpgsqlCommand(sql, connection)
            {
                Parameters =
                {
                    new("id", id),
                    new("sku", sku),
                    new("name", name),
                    new("ean", (object?)ean ?? DBNull.Value),
                    new("createdAt", DateTimeOffset.UtcNow)
                }
            };

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        return id;
    }
}
