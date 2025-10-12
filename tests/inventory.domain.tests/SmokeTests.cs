namespace CineBoutique.Inventory.Domain.Tests;

public class SmokeTests
{
    [Fact]
    public void DomainAssembly_Should_BeAccessible()
    {
        // Arrange & Act
        var assemblyMarker = typeof(CineBoutique.Inventory.Domain.DomainAssembly);

        // Assert
        Assert.NotNull(assemblyMarker);
    }
}
