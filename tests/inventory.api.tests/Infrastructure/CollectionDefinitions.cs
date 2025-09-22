using Xunit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

// Nom logique de la collection partagé par les tests
public static class TestCollections
{
    public const string Postgres = "PostgresDb";
}

// xUnit: définition de collection (le TYPE ne doit pas finir par "Collection")
[CollectionDefinition(TestCollections.Postgres)]
public sealed class PostgresDbFixtureCollectionDefinition : ICollectionFixture<PostgresTestContainerFixture>
{
}
