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
}
