using CineBoutique.Inventory.Domain.Counting;
using CineBoutique.Inventory.Domain.Tests.TestDoubles;

namespace CineBoutique.Inventory.Domain.Tests;

public sealed class ConflictResolutionTests
{
    [Fact]
    public void CountingRun_devrait_suivre_les_transitions_valides()
    {
        // Arrange
        var registry = new InMemoryActiveRunRegistry();
        var run = new CountingRun(Guid.NewGuid());

        // Act
        run.Start(registry);
        run.AddLine("1234567890123", 3);
        run.Complete(registry);
        var snapshot = run.CreateSnapshot();

        // Assert
        Assert.Equal(CountingRunStatus.Completed, run.Status);
        Assert.Equal(1, snapshot.Quantities.Count);
    }

    [Fact]
    public void CountingRun_devrait_refuser_completion_sans_ligne()
    {
        // Arrange
        var registry = new InMemoryActiveRunRegistry();
        var run = new CountingRun(Guid.NewGuid());
        run.Start(registry);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => run.Complete(registry));
    }

    [Fact]
    public void CountingRun_devrait_refuser_un_nouveau_demarrage()
    {
        // Arrange
        var registry = new InMemoryActiveRunRegistry();
        var run = new CountingRun(Guid.NewGuid());
        run.Start(registry);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => run.Start(registry));
    }

    [Fact]
    public void Registry_devrait_interdire_deux_runs_actifs_sur_la_meme_zone()
    {
        // Arrange
        var registry = new InMemoryActiveRunRegistry();
        var locationId = Guid.NewGuid();
        var firstRun = new CountingRun(locationId);
        var secondRun = new CountingRun(locationId);

        firstRun.Start(registry);
        firstRun.AddLine("1234567890123", 1);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => secondRun.Start(registry));

        // Assert
        Assert.Equal("Un run actif existe déjà pour cette zone.", exception.Message);

        // Cleanup
        firstRun.Complete(registry);
    }

    [Fact]
    public void Tracker_devrait_resoudre_un_conflit_apres_deux_comptes_identiques()
    {
        // Arrange
        var registry = new InMemoryActiveRunRegistry();
        var tracker = new CountingConflictTracker();
        var locationId = Guid.NewGuid();

        var firstSnapshot = CompleteRun(registry, locationId, 10);
        tracker.Evaluate(firstSnapshot, tolerance: 0);

        var secondSnapshot = CompleteRun(registry, locationId, 5);
        var secondEval = tracker.Evaluate(secondSnapshot, tolerance: 0);
        Assert.True(secondEval.HasConflicts);

        var thirdSnapshot = CompleteRun(registry, locationId, 5);

        // Act
        var thirdEval = tracker.Evaluate(thirdSnapshot, tolerance: 0);

        // Assert
        Assert.False(thirdEval.HasConflicts);
        Assert.DoesNotContain("1234567890123", thirdEval.ActiveConflicts);
    }

    private static CountingSnapshot CompleteRun(InMemoryActiveRunRegistry registry, Guid locationId, int quantity)
    {
        var run = new CountingRun(locationId);
        run.Start(registry);
        run.AddLine("1234567890123", quantity);
        run.Complete(registry);
        return run.CreateSnapshot();
    }
}
