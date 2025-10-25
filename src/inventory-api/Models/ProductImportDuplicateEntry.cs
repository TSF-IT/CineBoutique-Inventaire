using System.Collections.Generic;
using System.Collections.Immutable;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductImportDuplicateEntry(
    string Value,
    IReadOnlyList<int> Lines,
    IReadOnlyList<string> RawLines)
{
    public static ProductImportDuplicateEntry Create(
        string value,
        IEnumerable<int> lines,
        IEnumerable<string> rawLines)
    {
        return new ProductImportDuplicateEntry(
            value,
            ImmutableArray.CreateRange(lines),
            ImmutableArray.CreateRange(rawLines));
    }
}
