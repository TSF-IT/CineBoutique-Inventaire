using System;
using System.Net.Http;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Fixtures;

public sealed class InventoryApiFixture : IAsyncLifetime, IAsyncDisposable
{
    private PostgresContainerFixture? _postgres;
    private InventoryApiFactory _factory = default!;
    private NpgsqlDataSource _dataSource = default!;

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

        _dataSource = NpgsqlDataSource.Create(connectionString);
        _factory = new InventoryApiFactory(connectionString);

        await ApplyMigrationsAsync().ConfigureAwait(false);

        Seeder = new TestDataSeeder(_dataSource);

        using var client = _factory.CreateClient();
        await WaitUntilReadyAsync(client).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (!IsDockerAvailable)
        {
            return;
        }

        if (_factory is not null)
        {
            await _factory.DisposeAsync().ConfigureAwait(false);
        }

        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (IsDockerAvailable)
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public HttpClient CreateClient()
    {
        if (!IsDockerAvailable)
        {
            throw new InvalidOperationException("Impossible de créer un client HTTP lorsque Docker est indisponible.");
        }

        return _factory.CreateClient();
    }

    public async Task DbResetAsync()
    {
        if (!IsDockerAvailable)
        {
            return;
        }

        var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
        try
        {
            const string resetSql = "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;";
            var command = new NpgsqlCommand(resetSql, connection);
            try
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            finally
            {
                await command.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        await ApplyMigrationsAsync().ConfigureAwait(false);
    }

    private async Task ApplyMigrationsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        await Task.Run(() => runner.MigrateUp()).ConfigureAwait(false);
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

[CollectionDefinition("InventoryApi")]
public sealed class InventoryApiFixtureCollectionDefinition :
    ICollectionFixture<PostgresContainerFixture>,
    ICollectionFixture<InventoryApiFixture>
{
}
