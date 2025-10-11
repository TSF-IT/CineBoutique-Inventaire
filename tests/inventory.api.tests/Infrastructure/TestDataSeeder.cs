using Npgsql;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class TestDataSeeder(NpgsqlDataSource dataSource)
{
    private readonly NpgsqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    // using Npgsql; using System; using System.Threading.Tasks;

    public async Task<Guid> CreateShopAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name is required", nameof(name));

        await using var conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

        // 1) Insert idempotent (unicité sur LOWER(Name))
        const string insertSql = @"
        INSERT INTO ""Shop"" (""Name"")
        VALUES (@Name)
        ON CONFLICT DO NOTHING;";
        await using (var insert = new NpgsqlCommand(insertSql, conn))
        {
            insert.Parameters.AddWithValue("Name", name.Trim());
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // 2) Récupérer l'Id du shop (existant ou nouvellement créé)
        const string selectSql = @"
        SELECT ""Id""
        FROM ""Shop""
        WHERE LOWER(""Name"") = LOWER(@Name)
        ORDER BY ""Id""
        LIMIT 1;";
        await using (var select = new NpgsqlCommand(selectSql, conn))
        {
            select.Parameters.AddWithValue("Name", name.Trim());
            var result = await select.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is Guid id) return id;
        }

        throw new InvalidOperationException($"Shop '{name}' not found after insert/select.");
    }

    public async Task<Guid> CreateLocationAsync(Guid shopId, string code, string label)
    {
        if (shopId == Guid.Empty) throw new ArgumentException("shopId is empty", nameof(shopId));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("code is required", nameof(code));

        var codeUpper = code.Trim().ToUpperInvariant();
        await using var conn = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

        // 1) Déjà présent ? (aligné avec l'index UQ_Location_Shop_Code sur UPPER(Code))
        const string selectSql = @"
        SELECT ""Id""
        FROM ""Location""
        WHERE ""ShopId"" = @ShopId
          AND UPPER(""Code"") = @CodeUpper
        LIMIT 1;";
        await using (var select = new NpgsqlCommand(selectSql, conn))
        {
            select.Parameters.AddWithValue("ShopId", shopId);
            select.Parameters.AddWithValue("CodeUpper", codeUpper);
            var existing = await select.ExecuteScalarAsync().ConfigureAwait(false);
            if (existing is Guid id) return id;
        }

        // 2) Insert idempotent, compatible avec la contrainte (UPPER(Code))
        var newId = Guid.NewGuid();
        const string insertSql = @"
        INSERT INTO ""Location"" (""Id"", ""ShopId"", ""Code"", ""Label"")
        SELECT @Id, @ShopId, @CodeUpper, @Label
        WHERE NOT EXISTS (
            SELECT 1
            FROM ""Location""
            WHERE ""ShopId"" = @ShopId
              AND UPPER(""Code"") = @CodeUpper
        );";
        await using (var insert = new NpgsqlCommand(insertSql, conn))
        {
            insert.Parameters.AddWithValue("Id", newId);
            insert.Parameters.AddWithValue("ShopId", shopId);
            insert.Parameters.AddWithValue("CodeUpper", codeUpper);
            insert.Parameters.AddWithValue("Label", string.IsNullOrWhiteSpace(label) ? codeUpper : label.Trim());
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // 3) Retourner l'Id (existant ou inséré)
        await using (var select2 = new NpgsqlCommand(selectSql, conn))
        {
            select2.Parameters.AddWithValue("ShopId", shopId);
            select2.Parameters.AddWithValue("CodeUpper", codeUpper);
            var obj = await select2.ExecuteScalarAsync().ConfigureAwait(false);
            if (obj is Guid id) return id;
        }

        // Fallback (ne devrait pas arriver)
        return newId;
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
