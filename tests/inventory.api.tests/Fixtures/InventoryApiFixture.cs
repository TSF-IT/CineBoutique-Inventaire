using System;
using System.Net.Http;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Infrastructure.Migrations;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Fixtures;

public sealed class InventoryApiFixture : IAsyncLifetime, IAsyncDisposable
{
    private PostgresContainerFixture? _postgres;
    private InventoryApiFactory? _factory;
    private NpgsqlDataSource? _dataSource;
    private string? _connectionString;
    private bool _initialized;

    public TestDataSeeder Seeder { get; private set; } = default!;
    public bool IsDockerAvailable => GetOrCachePostgres().IsDatabaseAvailable;
    public string? SkipReason => GetOrCachePostgres().SkipReason;

    public async Task InitializeAsync()
    {
        var postgresFixture = GetOrCachePostgres();

        if (!IsDockerAvailable)
        {
            return;
        }

        var overrideConnectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION");
        var connectionString = string.IsNullOrWhiteSpace(overrideConnectionString)
            ? postgresFixture.ConnectionString
            : overrideConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("La chaîne de connexion de test doit être définie.");
        }

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");

        _connectionString = connectionString;
        _dataSource = NpgsqlDataSource.Create(connectionString);

        await ApplyMigrationsAsync().ConfigureAwait(false);

        _factory = new InventoryApiFactory(connectionString);

        using (var client = _factory.CreateClient())
        {
            await WaitUntilReadyAsync(client).ConfigureAwait(false);
        }

        Seeder = new TestDataSeeder(_dataSource);
        _initialized = true;

        await ResetDatabaseSchemaAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (!_initialized)
        {
            return;
        }

        if (_factory is not null)
        {
            await _factory.DisposeAsync().ConfigureAwait(false);
            _factory = null;
        }

        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
            _dataSource = null;
        }

        Seeder = null!;
        _connectionString = null;
        _initialized = false;
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_initialized)
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public HttpClient CreateClient()
    {
        EnsureInitialized();
        return _factory!.CreateClient();
    }

    public async Task DbResetAsync()
    {
        if (!IsDockerAvailable)
        {
            return;
        }

        EnsureInitialized();

        await ResetDatabaseSchemaAsync().ConfigureAwait(false);
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _dataSource is null || _factory is null || string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("La fixture InventoryApiFixture n'est pas initialisée.");
        }
    }

    private async Task ResetDatabaseSchemaAsync()
    {
        if (_dataSource is null)
        {
            throw new InvalidOperationException("Le DataSource Postgres n'est pas initialisé.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
        const string resetSql = "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;";
        await using var command = new NpgsqlCommand(resetSql, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        await ApplyMigrationsAsync().ConfigureAwait(false);
    }

    private async Task ApplyMigrationsAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("La chaîne de connexion de test n'est pas initialisée.");
        }

        using var serviceProvider = BuildMigrationServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        await Task.Run(() => runner.MigrateUp()).ConfigureAwait(false);
    }

    private ServiceProvider BuildMigrationServiceProvider()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("La chaîne de connexion de test n'est pas initialisée.");
        }

        var services = new ServiceCollection();

        services
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(_connectionString)
                .ScanIn(typeof(Program).Assembly, typeof(MigrationsAssemblyMarker).Assembly).For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole());

        services.Configure<SelectingProcessorAccessorOptions>(options =>
        {
            options.ProcessorId = "Postgres";
        });

        services
            .AddOptions<ProcessorOptions>()
            .Configure(options =>
            {
                options.Timeout = TimeSpan.FromSeconds(90);
                options.ProviderSwitches = string.Empty;
                options.PreviewOnly = false;
            });

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ProcessorOptions>>().Value);

        return services.BuildServiceProvider();
    }

    private PostgresContainerFixture GetOrCachePostgres()
    {
        if (_postgres is { } existing)
        {
            return existing;
        }

        var fixture = PostgresContainerFixture.Instance
            ?? throw new InvalidOperationException("PostgresContainerFixture n'est pas initialisée.");

        _postgres = fixture;
        return fixture;
    }

    private static async Task WaitUntilReadyAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await client.GetAsync(client.CreateRelativeUri("/ready")).ConfigureAwait(true);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(true);
        }

        // Last attempt with detailed error
        var finalResponse = await client.GetAsync(client.CreateRelativeUri("/ready")).ConfigureAwait(true);
        var payload = await finalResponse.Content.ReadAsStringAsync().ConfigureAwait(true);
        throw new InvalidOperationException($"API not ready. Status={finalResponse.StatusCode}, Payload={payload}");
    }
}
