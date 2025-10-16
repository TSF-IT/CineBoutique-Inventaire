using System;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class NullProductImportMetrics : IProductImportMetrics
{
    public void IncrementStarted()
    {
    }

    public void IncrementSucceeded(bool dryRun)
    {
    }

    public void IncrementFailed()
    {
    }

    public void ObserveDuration(TimeSpan duration, bool dryRun)
    {
    }
}
