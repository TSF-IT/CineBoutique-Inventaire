using System;
using System.Net.Http;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using Docker.DotNet;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Fixtures;

public sealed class InventoryApiFixture : IAsyncLifetime, IAsyncDisposable
{
    private PostgreSqlContainer _container = default!;
    private InventoryApiFactory _factory = default!;
    private NpgsqlDataSource _dataSource = default!;

    public TestDataSeeder Seeder { get; private set; } = default!;
    public bool IsDockerAvailable { get; private set; } = true;
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        if (ShouldSkipDockerTests())
        {
            DisableDocker("Tests Docker explicitement désactivés via CI_SKIP_DOCKER_TESTS.");
            return;
        }

        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("inventory_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithCleanUp(true)
                .Build();

            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (ArgumentException ex) when (string.Equals(ex.ParamName, "DockerEndpointAuthConfig", StringComparison.Ordinal))
        {
            DisableDocker("Daemon Docker indisponible (DockerEndpointAuthConfig).");
            return;
        }
        catch (DockerApiException ex)
        {
            DisableDocker($"Daemon Docker indisponible ({ex.GetType().Name}).");
            return;
        }
        catch (InvalidOperationException ex) when (IsDockerConnectivityIssue(ex))
        {
            DisableDocker($"Daemon Docker indisponible ({ex.GetType().Name}).");
            return;
        }

        var connectionString = _container.GetConnectionString();
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _factory = new InventoryApiFactory(connectionString);
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

        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
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

    public async Task ResetDatabaseAsync()
    {
        const string truncateSql = @"DO $$
DECLARE
    stmt text;
BEGIN
    SELECT string_agg(format('TRUNCATE TABLE %I.%I RESTART IDENTITY CASCADE', schemaname, tablename), '; ')
    INTO stmt
    FROM pg_tables
    WHERE schemaname = 'public'
      AND tablename NOT IN ('VersionInfo');

    IF stmt IS NOT NULL THEN
        EXECUTE stmt;
    END IF;
END $$;";

        if (!IsDockerAvailable)
        {
            return;
        }

        var connection = _dataSource.CreateConnection();
        await connection.OpenAsync().ConfigureAwait(false);
        try
        {
            using var command = new NpgsqlCommand(truncateSql, connection);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool ShouldSkipDockerTests()
    {
        var value = Environment.GetEnvironmentVariable("CI_SKIP_DOCKER_TESTS");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private void DisableDocker(string reason)
    {
        IsDockerAvailable = false;
        SkipReason = reason;
    }

    private static bool IsDockerConnectivityIssue(InvalidOperationException exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.Contains("Docker", StringComparison.OrdinalIgnoreCase)
            || message.Contains("container", StringComparison.OrdinalIgnoreCase);
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
public sealed class InventoryApiFixtureCollectionDefinition : ICollectionFixture<InventoryApiFixture>
{
}
