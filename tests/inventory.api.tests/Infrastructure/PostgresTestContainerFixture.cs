using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class PostgresTestContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; private set; } = default!;
    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        Container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("inventory_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCleanUp(true)
            .Build();

        await Container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Container.DisposeAsync().AsTask();
    }
}
