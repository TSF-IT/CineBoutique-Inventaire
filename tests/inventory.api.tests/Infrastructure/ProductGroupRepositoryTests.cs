using System;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Tests.Fixtures;
using CineBoutique.Inventory.Api.Tests.Infrastructure;
using CineBoutique.Inventory.Api.Tests.Infra;
using CineBoutique.Inventory.Infrastructure.Database.Products;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

[Collection("db")]
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

        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        var repository = new ProductGroupRepository(connection);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var parentId = await repository.EnsureGroupAsync("Cafe", "Grains", ct).ConfigureAwait(false);
        var duplicateId = await repository.EnsureGroupAsync("Cafe", "Grains", ct).ConfigureAwait(false);

        parentId.Should().NotBeNull();
        duplicateId.Should().Be(parentId);
    }
}
