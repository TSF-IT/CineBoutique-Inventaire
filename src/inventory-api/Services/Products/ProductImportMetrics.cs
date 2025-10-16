using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportMetrics : IProductImportMetrics
{
    private readonly Counter<long>? _startedCounter;
    private readonly Counter<long>? _succeededCounter;
    private readonly Counter<long>? _failedCounter;
    private readonly Histogram<double>? _durationHistogram;

    public ProductImportMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create("cineboutique.inventory.products");
        _startedCounter = meter.CreateCounter<long>("import_started");
        _succeededCounter = meter.CreateCounter<long>("import_succeeded");
        _failedCounter = meter.CreateCounter<long>("import_failed");
        _durationHistogram = meter.CreateHistogram<double>("import_duration_ms", unit: "ms");
    }

    public void IncrementStarted()
    {
        _startedCounter?.Add(1);
    }

    public void IncrementSucceeded(bool dryRun)
    {
        if (_succeededCounter is null)
        {
            return;
        }

        if (dryRun)
        {
            _succeededCounter.Add(1, new KeyValuePair<string, object?>("dry_run", true));
        }
        else
        {
            _succeededCounter.Add(1);
        }
    }

    public void IncrementFailed()
    {
        _failedCounter?.Add(1);
    }

    public void ObserveDuration(TimeSpan duration, bool dryRun)
    {
        if (_durationHistogram is null)
        {
            return;
        }

        var tags = dryRun
            ? new KeyValuePair<string, object?>[] { new("dry_run", true) }
            : Array.Empty<KeyValuePair<string, object?>>();

        _durationHistogram.Record(duration.TotalMilliseconds, tags);
    }
}
