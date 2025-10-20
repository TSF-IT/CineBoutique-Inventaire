using CineBoutique.Inventory.Api.Tests.Fixtures;
using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

[CollectionDefinition("ApiSerial", DisableParallelization = true)]
public sealed class ApiSerialCollection :
    ICollectionFixture<PostgresContainerFixture>,
    ICollectionFixture<InventoryApiFixture>,
    ICollectionFixture<DatabaseFixture>
{ }
