using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

[CollectionDefinition(nameof(PostgresCollection))]
public class PostgresCollection : ICollectionFixture<PostgresTestContainerFixture> { }

public static class TestEnvironments
{
    public const string Ci = "CI";
    public const string Test = "Test";
}
