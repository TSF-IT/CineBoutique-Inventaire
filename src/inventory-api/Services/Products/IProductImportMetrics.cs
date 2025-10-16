using System;

namespace CineBoutique.Inventory.Api.Services.Products;

public interface IProductImportMetrics
{
    void IncrementStarted();

    void IncrementSucceeded(bool dryRun);

    void IncrementFailed();

    void ObserveDuration(TimeSpan duration, bool dryRun);
}
