using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Helpers;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Infra;
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
    private readonly object _gate = new();

    private PostgresContainerFixture? _postgres;
    private InventoryApiFactory? _factory;
    private NpgsqlDataSource? _dataSource;
    private string? _connectionString;
    private string? _schemaName;
    private bool _initialized;

    public TestDataSeeder Seeder { get; private set; } = default!;
    public bool IsBackendAvailable => TestDbOptions.UseExternalDb || GetOrCachePostgres().IsDatabaseAvailable;
    public string? SkipReason => GetOrCachePostgres().SkipReason;
    public TestAuditLogger AuditLogger
    {
        get
        {
            EnsureInitialized();
            return _factory!.Services.GetRequiredService<TestAuditLogger>();
        }
    }

    // xUnit l’appelle si la fixture est enregistrée comme ICollectionFixture
    public async Task InitializeAsync()
    {
        if (!IsBackendAvailable)
            return;

        await EnsureReadyAsync().ConfigureAwait(false);
        // Reset initial pour partir propre sur la première classe qui l’utilise
        await DbResetAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (!_initialized)
            return;

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
        _schemaName = null;
        _initialized = false;
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_initialized)
            await DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Initialisation paresseuse et idempotente:
    /// - choisit la connection string (TEST_DB_CONN ou container)
    /// - applique les migrations
    /// - crée la factory et vérifie /ready
    /// - crée le seeder
    /// </summary>
    public async Task EnsureReadyAsync()
    {
        if (_initialized)
            return;

        // verrou léger pour éviter la double init en concurrence
        lock (_gate)
        {
            if (_initialized)
                return;
        }

        if (!IsBackendAvailable)
            return;

        string cs;
        if (TestDbOptions.UseExternalDb)
        {
            var externalCs = TestDbOptions.ExternalConnectionString
                ?? throw new InvalidOperationException("La chaîne de connexion externe doit être définie.");

            cs = BuildExternalConnectionString(externalCs, out var schemaName);
            _schemaName = schemaName;
        }
        else
        {
            cs = GetOrCachePostgres().ConnectionString;
            _schemaName = null;
        }

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("La chaîne de connexion de test doit être définie.");

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");
        // Ceinture + bretelles pour tout code qui lit la config via env:
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", cs);

        _connectionString = cs;
        _dataSource = NpgsqlDataSource.Create(cs);

        if (!string.IsNullOrWhiteSpace(_schemaName))
        {
            await EnsureSchemaExistsAsync(_schemaName!).ConfigureAwait(false);
        }

        await ApplyMigrationsAsync().ConfigureAwait(false);

        _factory = new InventoryApiFactory(cs);

        using (var client = _factory.CreateClient())
        {
            await WaitUntilReadyAsync(client).ConfigureAwait(false);
        }

        Seeder = new TestDataSeeder(_dataSource);

        lock (_gate) { _initialized = true; }
    }

    public HttpClient CreateClient()
    {
        EnsureInitialized();
        return _factory!.CreateClient();
    }

    public void ClearAuditLogs()
    {
        if (!IsBackendAvailable)
        {
            return;
        }

        EnsureInitialized();
        AuditLogger.Clear();
    }

    public IReadOnlyList<AuditLogEntry> DrainAuditLogs()
    {
        if (!IsBackendAvailable)
        {
            return Array.Empty<AuditLogEntry>();
        }

        EnsureInitialized();
        return AuditLogger.Drain();
    }

    public async Task DbResetAsync()
    {
        if (!IsBackendAvailable)
            return;

        await EnsureReadyAsync().ConfigureAwait(false);
        await ResetDatabaseSchemaAsync().ConfigureAwait(false);
        ClearAuditLogs();
    }

    public async Task ResetAndSeedAsync(Func<TestDataSeeder, Task> plan)
    {
        if (!IsBackendAvailable)
            return;

        ArgumentNullException.ThrowIfNull(plan);

        await DbResetAsync().ConfigureAwait(false);
        EnsureInitialized();

        await plan(Seeder).ConfigureAwait(false);
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _dataSource is null || _factory is null || string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("La fixture InventoryApiFixture n'est pas initialisée.");
    }

    private async Task ResetDatabaseSchemaAsync()
    {
        if (_dataSource is null)
            throw new InvalidOperationException("Le DataSource Postgres n'est pas initialisé.");

        await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

        if (TestDbOptions.UseExternalDb)
        {
            if (string.IsNullOrWhiteSpace(_schemaName))
                throw new InvalidOperationException("Le schéma de tests n'est pas initialisé.");

            var resetSql = $"DROP SCHEMA IF EXISTS \"{_schemaName}\" CASCADE; CREATE SCHEMA \"{_schemaName}\";";

            await using var resetCommand = new NpgsqlCommand(resetSql, connection);
            await resetCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        else
        {
            const string resetSql = "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;";
            await using var resetCommand = new NpgsqlCommand(resetSql, connection);
            await resetCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await ApplyMigrationsAsync().ConfigureAwait(false);
    }

    private async Task ApplyMigrationsAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("La chaîne de connexion de test n'est pas initialisée.");

        using var serviceProvider = BuildMigrationServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

        // MigrateUp n’est pas async, on le pousse dans un Task.Run contrôlé
        await Task.Run(() => runner.MigrateUp()).ConfigureAwait(false);
    }

    private ServiceProvider BuildMigrationServiceProvider()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("La chaîne de connexion de test n'est pas initialisée.");

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
        if (_postgres != null)
            return _postgres;

        var fixture = PostgresContainerFixture.Instance
            ?? throw new InvalidOperationException("PostgresContainerFixture n'est pas initialisée.");

        _postgres = fixture;
        return fixture;
    }

    private static string BuildExternalConnectionString(string baseConnectionString, out string schemaName)
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            throw new ArgumentException("La chaîne de connexion externe doit être fournie.", nameof(baseConnectionString));

        schemaName = $"it_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName
        };

        return builder.ConnectionString;
    }

    private async Task EnsureSchemaExistsAsync(string schemaName)
    {
        if (_dataSource is null)
            throw new InvalidOperationException("Le DataSource Postgres n'est pas initialisé.");

        await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
        var commandText = $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";";
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task WaitUntilReadyAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 20; attempt++) // 20 au lieu de 10
        {
            var response = await client.GetAsync(client.CreateRelativeUri("/ready")).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false); // 300 ms
        }

        var finalResponse = await client.GetAsync(client.CreateRelativeUri("/ready")).ConfigureAwait(false);
        var payload = await finalResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new InvalidOperationException($"API not ready. Status={finalResponse.StatusCode}, Payload={payload}");
    }

}
