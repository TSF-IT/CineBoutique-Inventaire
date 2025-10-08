using CineBoutique.Inventory.Api.Tests.Fixtures;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

[CollectionDefinition("api-tests", DisableParallelization = true)]
public sealed class ApiTestsCollectionDefinition
  : ICollectionFixture<PostgresContainerFixture>, ICollectionFixture<InventoryApiFixture> { }