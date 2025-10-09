using System;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Infra;
using Docker.DotNet;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Fixtures;

public sealed class PostgresContainerFixture : IAsyncLifetime, IAsyncDisposable
{
    private PostgreSqlContainer? _container;
    private NpgsqlDataSource? _probeDataSource;
    private bool _ownsContainer;

    public static PostgresContainerFixture? Instance { get; private set; }

    public string? SkipReason { get; private set; }

    public bool IsDatabaseAvailable { get; private set; }

    public string ConnectionString { get; private set; } = string.Empty;

    public bool IsExternalDatabase { get; private set; }

    public PostgresContainerFixture()
    {
        Instance = this;
    }

    public async Task InitializeAsync()
    {
        if (TestDbOptions.UseExternalDb)
        {
            ConnectionString = TestDbOptions.ExternalConnectionString!;
            IsExternalDatabase = true;
            IsDatabaseAvailable = true;
            return;
        }

        if (ShouldSkipDockerTests())
        {
            DisableDatabase("Tests Docker explicitement désactivés via CI_SKIP_DOCKER_TESTS.");
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
            _ownsContainer = true;

            ConnectionString = _container.GetConnectionString();
            _probeDataSource = NpgsqlDataSource.Create(ConnectionString);
            await WaitUntilDatabaseReadyAsync().ConfigureAwait(false);
            IsDatabaseAvailable = true;
        }
        catch (ArgumentException ex) when (string.Equals(ex.ParamName, "DockerEndpointAuthConfig", StringComparison.Ordinal))
        {
            DisableDatabase("Daemon Docker indisponible (DockerEndpointAuthConfig).");
        }
        catch (DockerApiException ex)
        {
            DisableDatabase($"Daemon Docker indisponible ({ex.GetType().Name}).");
        }
        catch (InvalidOperationException ex) when (IsDockerConnectivityIssue(ex))
        {
            DisableDatabase($"Daemon Docker indisponible ({ex.GetType().Name}).");
        }
        catch (PostgresException ex)
        {
            DisableDatabase($"Initialisation Postgres échouée ({ex.SqlState}).");
        }
        catch (NpgsqlException)
        {
            DisableDatabase("Initialisation Postgres échouée (connexion).");
        }
    }

    public async Task DisposeAsync()
    {
        if (_probeDataSource is not null)
        {
            await _probeDataSource.DisposeAsync().ConfigureAwait(false);
        }

        if (_ownsContainer && _container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }

        Instance = null;
    }

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        return new ValueTask(DisposeAsync());
    }

    private async Task WaitUntilDatabaseReadyAsync()
    {
        if (_probeDataSource is null)
        {
            return;
        }

        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var connection = await _probeDataSource.OpenConnectionAsync().ConfigureAwait(false);
                try
                {
                    var command = new NpgsqlCommand("SELECT 1;", connection);
                    try
                    {
                        await command.ExecuteScalarAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await command.DisposeAsync().ConfigureAwait(false);
                    }

                    return;
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (PostgresException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            }
            catch (NpgsqlException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Impossible de vérifier la disponibilité de la base Postgres de test.");
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

    private void DisableDatabase(string reason)
    {
        SkipReason = reason;
        IsDatabaseAvailable = false;
        ConnectionString = string.Empty;
    }

    private static bool IsDockerConnectivityIssue(InvalidOperationException exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.Contains("Docker", StringComparison.OrdinalIgnoreCase)
            || message.Contains("container", StringComparison.OrdinalIgnoreCase)
            || message.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }
}
