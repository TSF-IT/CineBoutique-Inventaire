using System;
using System.Net.Http;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using Npgsql;
using Xunit;

public sealed class TestApiFactory : IAsyncLifetime, IAsyncDisposable
{
  private readonly PostgresContainerFixture _postgres = new();
  private readonly InventoryApiFixture _inventory = new();
  private HttpClient? _client;
  private bool _disposed;
  private bool _initialized;
  private string? _skipReason;

  public bool IsAvailable => _skipReason is null && _initialized;
  public string? SkipReason => _skipReason;

  public HttpClient Client
  {
    get
    {
      Skip.If(!IsAvailable, _skipReason ?? "Backend d'intégration indisponible.");

      if (!_initialized || _client is null)
        throw new InvalidOperationException("La factory n'est pas initialisée.");

      return _client;
    }
  }

  public async Task InitializeAsync()
  {
    await _postgres.InitializeAsync().ConfigureAwait(false);
    if (!_postgres.IsDatabaseAvailable)
    {
      _skipReason = _postgres.SkipReason ?? "Backend d'intégration indisponible.";
      return;
    }

    await _inventory.InitializeAsync().ConfigureAwait(false);
    if (!_inventory.IsBackendAvailable)
    {
      _skipReason = _inventory.SkipReason ?? "Backend d'intégration indisponible.";
      return;
    }

    await _inventory.EnsureReadyAsync().ConfigureAwait(false);
    _client = _inventory.CreateClient();
    _initialized = true;
  }

  Task IAsyncLifetime.DisposeAsync() => DisposeCoreAsync();

  async ValueTask IAsyncDisposable.DisposeAsync()
  {
    await DisposeCoreAsync().ConfigureAwait(false);
  }

  public async Task<IDisposable> WithDbAsync(Func<NpgsqlConnection, Task> plan)
  {
    ArgumentNullException.ThrowIfNull(plan);
    Skip.If(!IsAvailable, _skipReason ?? "Backend d'intégration indisponible.");

    // Assure que l'host est démarré → migrations/seed exécutés (via AppSettings__SeedOnStartup=true)
    _ = await Client.GetAsync("/health");

    await _inventory.DbResetAsync().ConfigureAwait(false);

    var connection = await _inventory.OpenConnectionAsync().ConfigureAwait(false);

    // Garantit la contrainte UNIQUE("Code") utilisée par nos UPSERTs dans les Arrange de tests.
    const string __ensureProductGroupUnique = @"
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint WHERE conname = 'uq_productgroup_code'
  ) THEN
    ALTER TABLE ""ProductGroup""
    ADD CONSTRAINT uq_productgroup_code UNIQUE (""Code"");
  END IF;
END $$;";

    await global::Dapper.SqlMapper.ExecuteAsync(connection, __ensureProductGroupUnique).ConfigureAwait(false);

    // Garantit un index/contrainte unique exploitable par ON CONFLICT("Sku") dans les arranges
    const string __ensureProductSkuUniqueIndex = @"
  CREATE UNIQUE INDEX IF NOT EXISTS uq_product_sku_idx
  ON ""Product"" (""Sku"");";
    await global::Dapper.SqlMapper.ExecuteAsync(connection, __ensureProductSkuUniqueIndex).ConfigureAwait(false);
    try
    {
      await plan(connection).ConfigureAwait(false);
    }
    finally
    {
      await connection.DisposeAsync().ConfigureAwait(false);
    }

    return new ResetScope(_inventory);
  }

  private async Task DisposeCoreAsync()
  {
    if (_disposed)
      return;

    _disposed = true;

    if (_client is not null)
    {
      _client.Dispose();
      _client = null;
    }

    await _inventory.DisposeAsync().ConfigureAwait(false);
    await _postgres.DisposeAsync().ConfigureAwait(false);
  }

  private sealed class ResetScope : IDisposable
  {
    private readonly InventoryApiFixture _fixture;
    private bool _disposed;

    public ResetScope(InventoryApiFixture fixture) => _fixture = fixture;

    public void Dispose()
    {
      if (_disposed)
        return;

      _disposed = true;
      if (_fixture.IsBackendAvailable)
      {
        _fixture.DbResetAsync().GetAwaiter().GetResult();
      }
    }
  }

  // --- Helpers de DONNÉES pour les tests (idempotents, sans ON CONFLICT) ---

  private const string __sqlUpsertGroup = @"
WITH upsert AS (
  UPDATE ""ProductGroup""
  SET ""Label"" = @label
  WHERE ""Code"" = @code
  RETURNING ""Id""
)
INSERT INTO ""ProductGroup"" (""Code"",""Label"")
SELECT @code, @label
WHERE NOT EXISTS (SELECT 1 FROM upsert)
RETURNING ""Id"";";

  private const string __sqlUpsertProduct = @"
WITH upsert AS (
  UPDATE ""Product""
  SET ""Name"" = @name,
      ""Ean""  = @ean,
      ""GroupId"" = @gid,
      ""UpdatedAtUtc"" = NOW() AT TIME ZONE 'UTC'
  WHERE ""Sku"" = @sku
  RETURNING ""Sku""
)
INSERT INTO ""Product"" (""Sku"",""Name"",""Ean"",""GroupId"",""CreatedAtUtc"",""UpdatedAtUtc"")
SELECT @sku, @name, @ean, @gid, NOW() AT TIME ZONE 'UTC', NOW() AT TIME ZONE 'UTC'
WHERE NOT EXISTS (SELECT 1 FROM upsert);";

  public async System.Threading.Tasks.Task<long> UpsertGroupAsync(
    System.Data.IDbConnection conn, string code, string label,
    System.Threading.CancellationToken ct = default)
  {
    return await Dapper.SqlMapper.ExecuteScalarAsync<long>(
      conn,
      new Dapper.CommandDefinition(__sqlUpsertGroup, new { code, label }, cancellationToken: ct)
    ).ConfigureAwait(false);
  }

  public async System.Threading.Tasks.Task UpsertProductAsync(
    System.Data.IDbConnection conn, string sku, string name, string? ean, long? groupId,
    System.Threading.CancellationToken ct = default)
  {
    await Dapper.SqlMapper.ExecuteAsync(
      conn,
      new Dapper.CommandDefinition(__sqlUpsertProduct, new { sku, name, ean, gid = (object?)groupId ?? System.DBNull.Value }, cancellationToken: ct)
    ).ConfigureAwait(false);
  }
}
