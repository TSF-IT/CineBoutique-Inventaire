using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

internal sealed class InventoryTestDataBuilder
{
    private readonly IDbConnection _connection;
    private readonly CountingRunSchema _countingRunSchema;

    private InventoryTestDataBuilder(IDbConnection connection, CountingRunSchema schema)
    {
        _connection = connection;
        _countingRunSchema = schema;
    }

    public static async Task<InventoryTestDataBuilder> CreateAsync(
        IDbConnection connection,
        CancellationToken cancellationToken = default)
    {
        var schema = await CountingRunSchema.CreateAsync(connection, cancellationToken).ConfigureAwait(false);
        return new InventoryTestDataBuilder(connection, schema);
    }

    public async Task<ShopData> CreateShopAsync(
        Action<ShopBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new ShopBuilder();
        configure?.Invoke(builder);
        var model = builder.Build();

        const string sql =
            "INSERT INTO \"Shop\" (\"Id\", \"Name\") VALUES (@Id, @Name);";

        await ExecuteAsync(sql, model, cancellationToken).ConfigureAwait(false);

        return new ShopData(model.Id, model.Name);
    }

    public async Task<LocationData> CreateLocationAsync(
        ShopData shop,
        Action<LocationBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new LocationBuilder(shop.Id);
        configure?.Invoke(builder);
        var model = builder.Build();

        const string sql =
            "INSERT INTO \"Location\" (\"Id\", \"Code\", \"Label\", \"ShopId\") VALUES (@Id, @Code, @Label, @ShopId);";

        await ExecuteAsync(sql, model, cancellationToken).ConfigureAwait(false);

        return new LocationData(model.Id, model.ShopId, model.Code, model.Label);
    }

    public async Task<ShopUserData> CreateShopUserAsync(
        ShopData shop,
        Action<ShopUserBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new ShopUserBuilder(shop.Id);
        configure?.Invoke(builder);
        var model = builder.Build();

        const string sql =
            "INSERT INTO \"ShopUser\" (\"Id\", \"ShopId\", \"Login\", \"DisplayName\", \"IsAdmin\", \"Secret_Hash\", \"Disabled\") " +
            "VALUES (@Id, @ShopId, @Login, @DisplayName, @IsAdmin, @SecretHash, @Disabled);";

        await ExecuteAsync(sql, model, cancellationToken).ConfigureAwait(false);

        return new ShopUserData(model.Id, model.ShopId, model.Login, model.DisplayName, model.IsAdmin);
    }

    public async Task<InventorySessionData> CreateInventorySessionAsync(
        Action<InventorySessionBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new InventorySessionBuilder();
        configure?.Invoke(builder);
        var model = builder.Build();

        const string sql =
            "INSERT INTO \"InventorySession\" (\"Id\", \"Name\", \"StartedAtUtc\", \"CompletedAtUtc\") VALUES (@Id, @Name, @StartedAtUtc, @CompletedAtUtc);";

        await ExecuteAsync(sql, model, cancellationToken).ConfigureAwait(false);

        return new InventorySessionData(model.Id, model.Name, model.StartedAtUtc, model.CompletedAtUtc);
    }

    public async Task<CountingRunData> CreateCountingRunAsync(
        InventorySessionData session,
        LocationData location,
        Action<CountingRunBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new CountingRunBuilder(session.Id, location.Id);
        configure?.Invoke(builder);
        var model = builder.Build();

        var columns = "\"Id\", \"InventorySessionId\", \"LocationId\", \"CountType\", \"StartedAtUtc\"";
        var values = "@Id, @InventorySessionId, @LocationId, @CountType, @StartedAtUtc";

        if (model.CompletedAtUtc is not null)
        {
            columns += ", \"CompletedAtUtc\"";
            values += ", @CompletedAtUtc";
        }

        if (_countingRunSchema.HasOwnerUserId)
        {
            columns += ", \"OwnerUserId\"";
            values += ", @OwnerUserId";
        }

        if (_countingRunSchema.ShouldPersistOperatorDisplayName(model.OperatorDisplayName))
        {
            columns += ", \"OperatorDisplayName\"";
            values += ", @OperatorDisplayName";
        }

        var sql = $"INSERT INTO \"CountingRun\" ({columns}) VALUES ({values});";

        await ExecuteAsync(sql, model, cancellationToken).ConfigureAwait(false);

        return new CountingRunData(
            model.Id,
            model.InventorySessionId,
            model.LocationId,
            model.CountType,
            model.StartedAtUtc,
            model.CompletedAtUtc,
            _countingRunSchema.HasOwnerUserId ? model.OwnerUserId : null,
            _countingRunSchema.HasOperatorDisplayName ? model.OperatorDisplayName : null);
    }

    public async Task<ProductData> CreateProductAsync(
        Action<ProductBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new ProductBuilder();
        configure?.Invoke(builder);
        var model = builder.Build();

        const string sql =
            "INSERT INTO \"Product\" (\"Id\", \"Sku\", \"Name\", \"Ean\", \"CreatedAtUtc\") VALUES (@Id, @Sku, @Name, @Ean, @CreatedAtUtc);";

        await ExecuteAsync(sql, model, cancellationToken).ConfigureAwait(false);

        return new ProductData(model.Id, model.Sku, model.Name, model.Ean);
    }

    public async Task<CountLineData> CreateCountLineAsync(
        CountingRunData run,
        ProductData product,
        Action<CountLineBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new CountLineBuilder(run.Id, product.Id);
        configure?.Invoke(builder);
        var model = builder.Build();

        const string sql =
            "INSERT INTO \"CountLine\" (\"Id\", \"CountingRunId\", \"ProductId\", \"Quantity\", \"CountedAtUtc\") VALUES (@Id, @CountingRunId, @ProductId, @Quantity, @CountedAtUtc);";

        await ExecuteAsync(sql, model, cancellationToken).ConfigureAwait(false);

        return new CountLineData(model.Id, model.CountingRunId, model.ProductId, model.Quantity, model.CountedAtUtc);
    }

    public Task CreateConflictAsync(
        CountLineData countLine,
        Action<ConflictBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new ConflictBuilder(countLine.Id);
        configure?.Invoke(builder);
        var model = builder.Build();

        const string sql =
            "INSERT INTO \"Conflict\" (\"Id\", \"CountLineId\", \"Status\", \"Notes\", \"CreatedAtUtc\", \"ResolvedAtUtc\") VALUES (@Id, @CountLineId, @Status, @Notes, @CreatedAtUtc, @ResolvedAtUtc);";

        return ExecuteAsync(sql, model, cancellationToken);
    }

    private Task ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return _connection.ExecuteAsync(command);
    }

    #region Builders

    internal sealed class ShopBuilder
    {
        private Guid _id = Guid.NewGuid();
        private string _name = $"Boutique test {Guid.NewGuid():N}";

        public ShopBuilder WithId(Guid id)
        {
            _id = id;
            return this;
        }

        public ShopBuilder WithName(string name)
        {
            _name = name;
            return this;
        }

        public ShopModel Build() => new(_id, _name.Trim());
    }

    internal sealed record ShopModel(Guid Id, string Name);

    internal sealed class LocationBuilder
    {
        private Guid _id = Guid.NewGuid();
        private readonly Guid _shopId;
        private string _code = $"LOC-{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        private string _label = "Zone test";

        public LocationBuilder(Guid shopId)
        {
            _shopId = shopId;
        }

        public LocationBuilder WithId(Guid id)
        {
            _id = id;
            return this;
        }

        public LocationBuilder WithCode(string code)
        {
            _code = code;
            return this;
        }

        public LocationBuilder WithLabel(string label)
        {
            _label = label;
            return this;
        }

        public LocationModel Build() => new(_id, _code.Trim(), _label.Trim(), _shopId);
    }

    internal sealed record LocationModel(Guid Id, string Code, string Label, Guid ShopId);

    internal sealed class ShopUserBuilder
    {
        private Guid _id = Guid.NewGuid();
        private readonly Guid _shopId;
        private string _login = $"user_{Guid.NewGuid():N}";
        private string _displayName = "Opérateur Test";
        private bool _isAdmin;
        private bool _disabled;
        private string _secretHash = string.Empty;

        public ShopUserBuilder(Guid shopId)
        {
            _shopId = shopId;
        }

        public ShopUserBuilder WithId(Guid id)
        {
            _id = id;
            return this;
        }

        public ShopUserBuilder WithLogin(string login)
        {
            _login = login;
            return this;
        }

        public ShopUserBuilder WithDisplayName(string displayName)
        {
            _displayName = displayName;
            return this;
        }

        public ShopUserBuilder AsAdmin(bool isAdmin = true)
        {
            _isAdmin = isAdmin;
            return this;
        }

        public ShopUserBuilder Disabled(bool disabled = true)
        {
            _disabled = disabled;
            return this;
        }

        public ShopUserBuilder WithSecretHash(string secretHash)
        {
            _secretHash = secretHash;
            return this;
        }

        public ShopUserModel Build() => new(
            _id,
            _shopId,
            _login.Trim(),
            _displayName.Trim(),
            _isAdmin,
            _secretHash,
            _disabled);
    }

    internal sealed record ShopUserModel(
        Guid Id,
        Guid ShopId,
        string Login,
        string DisplayName,
        bool IsAdmin,
        string SecretHash,
        bool Disabled);

    internal sealed class InventorySessionBuilder
    {
        private Guid _id = Guid.NewGuid();
        private string _name = "Session test";
        private DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        private DateTimeOffset? _completedAtUtc;

        public InventorySessionBuilder WithId(Guid id)
        {
            _id = id;
            return this;
        }

        public InventorySessionBuilder WithName(string name)
        {
            _name = name;
            return this;
        }

        public InventorySessionBuilder StartedAt(DateTimeOffset startedAtUtc)
        {
            _startedAtUtc = startedAtUtc;
            return this;
        }

        public InventorySessionBuilder CompletedAt(DateTimeOffset? completedAtUtc)
        {
            _completedAtUtc = completedAtUtc;
            return this;
        }

        public InventorySessionModel Build() => new(_id, _name.Trim(), _startedAtUtc, _completedAtUtc);
    }

    internal sealed record InventorySessionModel(
        Guid Id,
        string Name,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc);

    internal sealed class CountingRunBuilder
    {
        private Guid _id = Guid.NewGuid();
        private readonly Guid _sessionId;
        private readonly Guid _locationId;
        private short _countType = 1;
        private DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        private DateTimeOffset? _completedAtUtc;
        private Guid? _ownerUserId;
        private string? _operatorDisplayName;

        public CountingRunBuilder(Guid sessionId, Guid locationId)
        {
            _sessionId = sessionId;
            _locationId = locationId;
        }

        public CountingRunBuilder WithId(Guid id)
        {
            _id = id;
            return this;
        }

        public CountingRunBuilder WithCountType(short countType)
        {
            _countType = countType;
            return this;
        }

        public CountingRunBuilder StartedAt(DateTimeOffset startedAtUtc)
        {
            _startedAtUtc = startedAtUtc;
            return this;
        }

        public CountingRunBuilder CompletedAt(DateTimeOffset? completedAtUtc)
        {
            _completedAtUtc = completedAtUtc;
            return this;
        }

        public CountingRunBuilder WithOwner(Guid? ownerUserId)
        {
            _ownerUserId = ownerUserId;
            return this;
        }

        public CountingRunBuilder WithOperatorDisplayName(string? displayName)
        {
            _operatorDisplayName = displayName;
            return this;
        }

        public CountingRunModel Build()
        {
            return new CountingRunModel(
                _id,
                _sessionId,
                _locationId,
                _countType,
                _startedAtUtc,
                _completedAtUtc,
                _ownerUserId,
                _operatorDisplayName);
        }
    }

    internal sealed record CountingRunModel(
        Guid Id,
        Guid InventorySessionId,
        Guid LocationId,
        short CountType,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        Guid? OwnerUserId,
        string? OperatorDisplayName);

    internal sealed class ProductBuilder
    {
        private Guid _id = Guid.NewGuid();
        private string _sku = $"SKU-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        private string _name = "Produit test";
        private string? _ean;
        private DateTimeOffset _createdAtUtc = DateTimeOffset.UtcNow.AddDays(-1);

        public ProductBuilder WithId(Guid id)
        {
            _id = id;
            return this;
        }

        public ProductBuilder WithSku(string sku)
        {
            _sku = sku;
            return this;
        }

        public ProductBuilder WithName(string name)
        {
            _name = name;
            return this;
        }

        public ProductBuilder WithEan(string? ean)
        {
            _ean = ean;
            return this;
        }

        public ProductBuilder CreatedAt(DateTimeOffset createdAtUtc)
        {
            _createdAtUtc = createdAtUtc;
            return this;
        }

        public ProductModel Build() => new(
            _id,
            _sku.Trim(),
            _name.Trim(),
            string.IsNullOrWhiteSpace(_ean) ? null : _ean.Trim(),
            _createdAtUtc);
    }

    internal sealed record ProductModel(
        Guid Id,
        string Sku,
        string Name,
        string? Ean,
        DateTimeOffset CreatedAtUtc);

    internal sealed class CountLineBuilder
    {
        private Guid _id = Guid.NewGuid();
        private readonly Guid _runId;
        private readonly Guid _productId;
        private decimal _quantity = 1m;
        private DateTimeOffset _countedAtUtc = DateTimeOffset.UtcNow;

        public CountLineBuilder(Guid runId, Guid productId)
        {
            _runId = runId;
            _productId = productId;
        }

        public CountLineBuilder WithId(Guid id)
        {
            _id = id;
            return this;
        }

        public CountLineBuilder WithQuantity(decimal quantity)
        {
            _quantity = quantity;
            return this;
        }

        public CountLineBuilder CountedAt(DateTimeOffset countedAtUtc)
        {
            _countedAtUtc = countedAtUtc;
            return this;
        }

        public CountLineModel Build() => new(_id, _runId, _productId, _quantity, _countedAtUtc);
    }

    internal sealed record CountLineModel(
        Guid Id,
        Guid CountingRunId,
        Guid ProductId,
        decimal Quantity,
        DateTimeOffset CountedAtUtc);

    internal sealed class ConflictBuilder
    {
        private Guid _id = Guid.NewGuid();
        private readonly Guid _countLineId;
        private string _status = "open";
        private string? _notes;
        private DateTimeOffset _createdAtUtc = DateTimeOffset.UtcNow;
        private DateTimeOffset? _resolvedAtUtc;

        public ConflictBuilder(Guid countLineId)
        {
            _countLineId = countLineId;
        }

        public ConflictBuilder WithId(Guid id)
        {
            _id = id;
            return this;
        }

        public ConflictBuilder WithStatus(string status)
        {
            _status = status;
            return this;
        }

        public ConflictBuilder WithNotes(string? notes)
        {
            _notes = notes;
            return this;
        }

        public ConflictBuilder CreatedAt(DateTimeOffset createdAtUtc)
        {
            _createdAtUtc = createdAtUtc;
            return this;
        }

        public ConflictBuilder ResolvedAt(DateTimeOffset? resolvedAtUtc)
        {
            _resolvedAtUtc = resolvedAtUtc;
            return this;
        }

        public ConflictModel Build() => new(
            _id,
            _countLineId,
            _status.Trim(),
            string.IsNullOrWhiteSpace(_notes) ? null : _notes.Trim(),
            _createdAtUtc,
            _resolvedAtUtc);
    }

    internal sealed record ConflictModel(
        Guid Id,
        Guid CountLineId,
        string Status,
        string? Notes,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ResolvedAtUtc);

    #endregion

    #region Schema

    private sealed record CountingRunSchema(
        bool HasOperatorDisplayName,
        bool OperatorDisplayNameIsNullable,
        bool HasOwnerUserId)
    {
        public static async Task<CountingRunSchema> CreateAsync(
            IDbConnection connection,
            CancellationToken cancellationToken)
        {
            var hasOperator = await ColumnExistsAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken)
                .ConfigureAwait(false);
            var operatorNullable = hasOperator && await ColumnIsNullableAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken)
                .ConfigureAwait(false);
            var hasOwner = await ColumnExistsAsync(connection, "CountingRun", "OwnerUserId", cancellationToken)
                .ConfigureAwait(false);

            return new CountingRunSchema(hasOperator, operatorNullable, hasOwner);
        }

        public bool ShouldPersistOperatorDisplayName(string? operatorDisplayName)
        {
            if (!HasOperatorDisplayName)
            {
                return false;
            }

            if (OperatorDisplayNameIsNullable)
            {
                return !string.IsNullOrWhiteSpace(operatorDisplayName);
            }

            return true;
        }

        private static Task<bool> ColumnExistsAsync(
            IDbConnection connection,
            string table,
            string column,
            CancellationToken cancellationToken)
        {
            const string sql =
                "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE LOWER(table_name) = LOWER(@Table) AND LOWER(column_name) = LOWER(@Column) AND table_schema = ANY (current_schemas(TRUE)));";

            var command = new CommandDefinition(sql, new { Table = table, Column = column }, cancellationToken: cancellationToken);
            return connection.ExecuteScalarAsync<bool>(command);
        }

        private static Task<bool> ColumnIsNullableAsync(
            IDbConnection connection,
            string table,
            string column,
            CancellationToken cancellationToken)
        {
            const string sql =
                "SELECT CASE WHEN COUNT(*) = 0 THEN TRUE ELSE BOOL_OR(is_nullable = 'YES') END FROM information_schema.columns WHERE LOWER(table_name) = LOWER(@Table) AND LOWER(column_name) = LOWER(@Column) AND table_schema = ANY (current_schemas(TRUE));";

            var command = new CommandDefinition(sql, new { Table = table, Column = column }, cancellationToken: cancellationToken);
            return connection.ExecuteScalarAsync<bool>(command);
        }
    }

    #endregion
}

internal readonly record struct ShopData(Guid Id, string Name);

internal readonly record struct LocationData(Guid Id, Guid ShopId, string Code, string Label);

internal readonly record struct ShopUserData(Guid Id, Guid ShopId, string Login, string DisplayName, bool IsAdmin);

internal readonly record struct InventorySessionData(
    Guid Id,
    string Name,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

internal readonly record struct CountingRunData(
    Guid Id,
    Guid InventorySessionId,
    Guid LocationId,
    short CountType,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    Guid? OwnerUserId,
    string? OperatorDisplayName);

internal readonly record struct ProductData(Guid Id, string Sku, string Name, string? Ean);

internal readonly record struct CountLineData(Guid Id, Guid CountingRunId, Guid ProductId, decimal Quantity, DateTimeOffset CountedAtUtc);
