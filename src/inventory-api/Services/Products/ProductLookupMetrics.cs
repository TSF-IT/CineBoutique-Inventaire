using System;
using System.Diagnostics.Metrics;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductLookupMetrics : IProductLookupMetrics, IDisposable
{
    internal const string MeterName = ProductImportMetrics.MeterName;

    private readonly Meter _meter;
    private readonly Counter<long>? _ambiguityCounter;

    public ProductLookupMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _meter = meterFactory.Create(MeterName);
        _ambiguityCounter = _meter.CreateCounter<long>("lookup_ambiguity_total");
    }

    public void IncrementAmbiguity()
    {
        _ambiguityCounter?.Add(1);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
