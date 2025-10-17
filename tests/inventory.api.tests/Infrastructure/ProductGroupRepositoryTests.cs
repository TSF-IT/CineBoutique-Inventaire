using System;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infra;
using CineBoutique.Inventory.Infrastructure.Database;
using CineBoutique.Inventory.Infrastructure.Database.Products;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

[Collection("api-tests")]
public sealed class ProductGroupRepositoryTests : IntegrationTestBase
{
    public ProductGroupRepositoryTests(InventoryApiFixture fixture)
    {
        UseFixture(fixture);
    }

    [SkippableFact]
    public async Task EnsureGroupAsync_SameInputs_ReturnSameIdentifier()
    {
        Skip.IfNot(TestEnvironment.IsIntegrationBackendAvailable(), "Backend d'intégration indisponible.");

        await Fixture.ResetAndSeedAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        var factory = new TestConnectionFactory(Fixture.ConnectionString);
        var repository = new ProductGroupRepository(factory);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var parentId = await repository.EnsureGroupAsync("Cafe", "Grains", ct).ConfigureAwait(false);
        var duplicateId = await repository.EnsureGroupAsync("Cafe", "Grains", ct).ConfigureAwait(false);

        parentId.Should().NotBeNull();
        duplicateId.Should().Be(parentId);
    }

    private sealed class TestConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public TestConnectionFactory(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public NpgsqlConnection CreateConnection()
        {
            return new NpgsqlConnection(_connectionString);
        }

        public async Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
        {
            var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
    }
}
