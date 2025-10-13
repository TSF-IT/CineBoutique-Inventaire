using CineBoutique.Inventory.Domain.Counting;
using CineBoutique.Inventory.Domain.Tests.TestDoubles;

namespace CineBoutique.Inventory.Domain.Tests;

public sealed class CountingRulesTests
{
    private readonly InMemoryActiveRunRegistry _registry = new();
    private readonly Guid _locationId = Guid.NewGuid();

    [Fact]
    public void AggregateByEan_devrait_sommer_les_quantites_par_code()
    {
        // Arrange
        var run = CreateRun();
        run.Start(_registry);
        run.AddLine("1234567890123", 10);
        run.AddLine("1234567890123", 5);
        run.AddLine("9876543210987", 2);

        // Act
        var aggregated = run.AggregateByEan();

        // Assert
        Assert.Equal(15, aggregated["1234567890123"]);
        Assert.Equal(2, aggregated["9876543210987"]);
    }

    [Fact]
    public void Tolerance_zero_devrait_declencher_un_conflit_sur_ecart()
    {
        // Arrange
        var tracker = new CountingConflictTracker();
        var firstRun = CompleteRun(5);
        var secondRun = CompleteRun(7);

        // Act
        tracker.Evaluate(firstRun, tolerance: 0);
        var evaluation = tracker.Evaluate(secondRun, tolerance: 0);

        // Assert
        Assert.True(evaluation.HasConflicts);
        Assert.Contains("1234567890123", evaluation.ActiveConflicts);
    }

    [Fact]
    public void Tolerance_positive_devrait_absorber_un_petit_ecart()
    {
        // Arrange
        var tracker = new CountingConflictTracker();
        var firstRun = CompleteRun(10);
        var secondRun = CompleteRun(11);

        // Act
        tracker.Evaluate(firstRun, tolerance: 2);
        var evaluation = tracker.Evaluate(secondRun, tolerance: 2);

        // Assert
        Assert.False(evaluation.HasConflicts);
    }

    [Fact]
    public void AddLine_devrait_refuser_une_quantite_negative()
    {
        // Arrange
        var run = CreateRun();
        run.Start(_registry);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => run.AddLine("1234567890123", -1));
    }

    [Fact]
    public void AggregateByEan_devrait_detecter_un_overflow()
    {
        // Arrange
        var run = CreateRun();
        run.Start(_registry);
        run.AddLine("1234567890123", int.MaxValue);
        run.AddLine("1234567890123", 1);

        // Act & Assert
        Assert.Throws<OverflowException>(() => run.AggregateByEan());
    }

    private CountingSnapshot CompleteRun(int quantity)
    {
        var run = CreateRun();
        run.Start(_registry);
        run.AddLine("1234567890123", quantity);
        run.Complete(_registry);
        return run.CreateSnapshot();
    }

    private CountingRun CreateRun() => new(_locationId);
}
