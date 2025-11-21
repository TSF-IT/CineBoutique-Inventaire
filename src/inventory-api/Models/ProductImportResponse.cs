using System.Collections.Immutable;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductImportResponse(
    int Total,
    int Inserted,
    int Updated,
    int WouldInsert,
    int WouldUpdate,
    int ErrorCount,
    bool DryRun,
    bool Skipped,
    IReadOnlyList<ProductImportError> Errors,
    IReadOnlyCollection<string> UnknownColumns,
    IReadOnlyCollection<ProductImportGroupProposal> ProposedGroups,
    IReadOnlyList<ProductImportSkippedLine> SkippedLines,
    ProductImportDuplicateReport Duplicates)
{
    public static ProductImportResponse Success(
        int total,
        int inserted,
        int updated,
        IReadOnlyCollection<string> unknownColumns,
        IReadOnlyCollection<ProductImportGroupProposal> proposedGroups,
        IReadOnlyList<ProductImportSkippedLine> skippedLines,
        ProductImportDuplicateReport duplicates) =>
        new(
            total,
            inserted,
            updated,
            WouldInsert: 0,
            WouldUpdate: 0,
            ErrorCount: 0,
            DryRun: false,
            Skipped: false,
            ImmutableArray<ProductImportError>.Empty,
            unknownColumns,
            proposedGroups,
            skippedLines,
            duplicates);

    public static ProductImportResponse DryRunResult(
        int total,
        int wouldInsert,
        int wouldUpdate,
        IReadOnlyCollection<string> unknownColumns,
        IReadOnlyCollection<ProductImportGroupProposal> proposedGroups,
        IReadOnlyList<ProductImportSkippedLine> skippedLines,
        ProductImportDuplicateReport duplicates) =>
        new(
            total,
            Inserted: 0,
            Updated: 0,
            WouldInsert: wouldInsert,
            WouldUpdate: wouldUpdate,
            ErrorCount: 0,
            DryRun: true,
            Skipped: false,
            ImmutableArray<ProductImportError>.Empty,
            unknownColumns,
            proposedGroups,
            skippedLines,
            duplicates);

    public static ProductImportResponse Failure(
        int total,
        IReadOnlyList<ProductImportError> errors,
        IReadOnlyCollection<string> unknownColumns,
        IReadOnlyCollection<ProductImportGroupProposal> proposedGroups,
        IReadOnlyList<ProductImportSkippedLine> skippedLines,
        ProductImportDuplicateReport duplicates)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return new(
            total,
            Inserted: 0,
            Updated: 0,
            WouldInsert: 0,
            WouldUpdate: 0,
            errors.Count,
            DryRun: false,
            Skipped: false,
            errors,
            unknownColumns,
            proposedGroups,
            skippedLines,
            duplicates);
    }

    public static ProductImportResponse SkippedResult() =>
        new(
            0,
            0,
            0,
            0,
            0,
            0,
            DryRun: false,
            Skipped: true,
            ImmutableArray<ProductImportError>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<ProductImportGroupProposal>.Empty,
            ImmutableArray<ProductImportSkippedLine>.Empty,
            ProductImportDuplicateReport.Empty);
}
