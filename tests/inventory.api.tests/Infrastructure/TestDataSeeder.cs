using Npgsql;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class TestDataSeeder(NpgsqlDataSource dataSource)
{
    private readonly NpgsqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    // using Npgsql; using System; using System.Threading.Tasks;

    public async Task<Guid> CreateShopAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

        // 1) Tente l'insert en s'appuyant sur l'INDEX UNIQUE d'expression (LOWER("Name"))
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
            INSERT INTO ""Shop"" (""Name"")
            VALUES (@name)
            ON CONFLICT (LOWER(""Name"")) DO NOTHING
            RETURNING ""Id"";";
            cmd.Parameters.AddWithValue("name", name);

            var inserted = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (inserted is Guid id) return id;
        }

        // 2) Récupère l'existant si conflit
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ""Id""
                            FROM ""Shop""
                            WHERE LOWER(""Name"") = LOWER(@name)
                            LIMIT 1;";
            cmd.Parameters.AddWithValue("name", name);

            var existing = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (existing is Guid id) return id;
        }

        throw new InvalidOperationException($"Unable to create or find shop '{name}'.");
    }


    public async Task<Guid> CreateLocationAsync(Guid shopId, string code, string label)
    {
        await using var conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
            INSERT INTO ""Location"" (""Code"", ""Label"", ""ShopId"")
            VALUES (@code, @label, @shopId)
            ON CONFLICT (""ShopId"", UPPER(""Code"")) DO NOTHING
            RETURNING ""Id"";";
            cmd.Parameters.AddWithValue("code", code);
            cmd.Parameters.AddWithValue("label", label);
            cmd.Parameters.AddWithValue("shopId", shopId);

            var inserted = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (inserted is Guid id) return id;
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT ""Id""
            FROM ""Location""
            WHERE ""ShopId"" = @shopId
              AND UPPER(""Code"") = UPPER(@code)
            LIMIT 1;";
            cmd.Parameters.AddWithValue("shopId", shopId);
            cmd.Parameters.AddWithValue("code", code);

            var existing = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (existing is Guid id) return id;
        }

        throw new InvalidOperationException($"Unable to create or find location '{code}' for shop {shopId}.");
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
