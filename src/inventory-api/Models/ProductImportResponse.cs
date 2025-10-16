using System.Collections.Immutable;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductImportResponse(
    int Total,
    int Inserted,
    int WouldInsert,
    int ErrorCount,
    bool DryRun,
    bool Skipped,
    IReadOnlyList<ProductImportError> Errors)
{
    public static ProductImportResponse Success(int total, int inserted) =>
        new(total, inserted, inserted, 0, DryRun: false, Skipped: false, ImmutableArray<ProductImportError>.Empty);

    public static ProductImportResponse DryRunResult(int total, int wouldInsert) =>
        new(total, Inserted: 0, wouldInsert, 0, DryRun: true, Skipped: false, ImmutableArray<ProductImportError>.Empty);

    public static ProductImportResponse Failure(int total, IReadOnlyList<ProductImportError> errors, int wouldInsert = 0) =>
        new(total, Inserted: 0, wouldInsert, errors.Count, DryRun: false, Skipped: false, errors);

    public static ProductImportResponse SkippedResult() =>
        new(0, 0, 0, 0, DryRun: false, Skipped: true, ImmutableArray<ProductImportError>.Empty);
}
