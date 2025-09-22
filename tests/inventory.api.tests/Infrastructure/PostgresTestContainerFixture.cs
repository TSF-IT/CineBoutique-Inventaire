using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public class PostgresTestContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("cineboutique_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync() => await Container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await Container.DisposeAsync().ConfigureAwait(false);
}

[CollectionDefinition("ApiTestCollection")]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "xUnit collection pattern.")]
public class ApiTestCollection : ICollectionFixture<PostgresTestContainerFixture> { }
