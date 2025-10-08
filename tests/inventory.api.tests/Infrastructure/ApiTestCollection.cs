using CineBoutique.Inventory.Api.Tests.Fixtures;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

[CollectionDefinition("api-tests", DisableParallelization = true)]
public sealed class ApiTestCollection :
    ICollectionFixture<PostgresContainerFixture>,
    ICollectionFixture<InventoryApiFixture>
{
}
