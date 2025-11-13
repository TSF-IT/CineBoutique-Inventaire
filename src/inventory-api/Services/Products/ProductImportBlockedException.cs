using System;
using System.Collections.Generic;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportBlockedException : Exception
{
    public ProductImportBlockedException(Guid? locationId, IReadOnlyList<Guid> sampleProductIds)
        : base("Suppression impossible")
    {
        LocationId = locationId;
        SampleProductIds = sampleProductIds ?? Array.Empty<Guid>();
    }

    public Guid? LocationId { get; }

    public IReadOnlyList<Guid> SampleProductIds { get; }
}
