using System.Collections.Immutable;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductImportResponse(
    int Total,
    int Created,
    int Updated,
    int ErrorCount,
    bool DryRun,
    bool Skipped,
    IReadOnlyList<ProductImportError> Errors,
    IReadOnlyCollection<string> UnknownColumns)
{
    public static ProductImportResponse Success(int total, int created, int updated, IReadOnlyCollection<string> unknownColumns) =>
        new(total, created, updated, 0, DryRun: false, Skipped: false, ImmutableArray<ProductImportError>.Empty, unknownColumns);

    public static ProductImportResponse DryRunResult(
        int total,
        int created,
        int updated,
        IReadOnlyCollection<string> unknownColumns) =>
        new(total, Created: created, Updated: updated, 0, DryRun: true, Skipped: false, ImmutableArray<ProductImportError>.Empty, unknownColumns);

    public static ProductImportResponse Failure(
        int total,
        IReadOnlyList<ProductImportError> errors,
        IReadOnlyCollection<string> unknownColumns) =>
        new(total, Created: 0, Updated: 0, errors.Count, DryRun: false, Skipped: false, errors, unknownColumns);

    public static ProductImportResponse SkippedResult() =>
        new(0, 0, 0, 0, DryRun: false, Skipped: true, ImmutableArray<ProductImportError>.Empty, ImmutableArray<string>.Empty);
}
