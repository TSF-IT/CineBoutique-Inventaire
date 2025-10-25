using System.Collections.Generic;
using System.Collections.Immutable;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductImportDuplicateReport(
    IReadOnlyList<ProductImportDuplicateEntry> Skus,
    IReadOnlyList<ProductImportDuplicateEntry> Eans)
{
    public static ProductImportDuplicateReport Empty { get; } = new(
        ImmutableArray<ProductImportDuplicateEntry>.Empty,
        ImmutableArray<ProductImportDuplicateEntry>.Empty);

    public static ProductImportDuplicateReport Create(
        IEnumerable<ProductImportDuplicateEntry> skus,
        IEnumerable<ProductImportDuplicateEntry> eans) =>
        new(
            ImmutableArray.CreateRange(skus),
            ImmutableArray.CreateRange(eans));
}
