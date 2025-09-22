using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Xunit;

[CollectionDefinition("ApiTestsCollection", DisableParallelization = true)]
public class ApiTestsCollection : ICollectionFixture<TestPostgresFixture> { }

public class TestPostgresFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = default!;
    private PostgreSqlTestcontainer _pg = default!;

    public async Task InitializeAsync()
    {
        var overrideConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (!string.IsNullOrWhiteSpace(overrideConnectionString))
        {
            ConnectionString = overrideConnectionString;
            return;
        }

        var pg = new TestcontainersBuilder<PostgreSqlTestcontainer>()
            .WithDatabase(new PostgreSqlTestcontainerConfiguration
            {
                Database = "cineboutique_test",
                Username = "postgres",
                Password = "postgres"
            })
            .WithImage("postgres:16-alpine")
            .WithName($"cb-inventory-test-pg-{Guid.NewGuid():N}")
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await pg.StartAsync();
        _pg = pg;

        var host = "127.0.0.1";
        var mappedPort = _pg.GetMappedPublicPort(5432);
        ConnectionString = $"Host={host};Port={mappedPort};Database=cineboutique_test;Username=postgres;Password=postgres;Pooling=false;";

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    public async Task DisposeAsync()
    {
        if (_pg != null)
        {
            await _pg.StopAsync();
            await _pg.DisposeAsync();
        }
    }
}
