using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public class PostgresTestContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; private set; } =
        new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("cineboutique_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Container.StartAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await Container.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Collection unique pour partager le container PG entre tous les tests d’API.
/// </summary>
[CollectionDefinition("ApiTestCollection")]
public class ApiTestCollection : ICollectionFixture<PostgresTestContainerFixture> { }
