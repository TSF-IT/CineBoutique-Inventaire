using CineBoutique.Inventory.Api.Tests.Infrastructure;
using Xunit;

[CollectionDefinition("ApiTestsCollection", DisableParallelization = true)]
public sealed class ApiTestsCollection : ICollectionFixture<InventoryApiApplicationFactory>
{
}
