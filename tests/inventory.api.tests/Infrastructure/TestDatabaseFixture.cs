using System.Net.Http;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class TestDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlTestcontainer _container;
    public bool IsDockerAvailable { get; private set; } = true;

    public TestDatabaseFixture()
    {
        _container = new TestcontainersBuilder<PostgreSqlTestcontainer>()
            .WithDatabase(new PostgreSqlTestcontainerConfiguration
            {
                Database = "inventory_tests",
                Username = "postgres",
                Password = "postgres"
            })
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    public string ConnectionString => _container.ConnectionString;

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            IsDockerAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (IsDockerAvailable)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}
