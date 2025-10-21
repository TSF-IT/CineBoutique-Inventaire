using System;
using System.Collections.Immutable;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductImportResponse(
    int Total,
    int Inserted,
    int Updated,
    int WouldInsert,
    int ErrorCount,
    bool DryRun,
    bool Skipped,
    IReadOnlyList<ProductImportError> Errors,
    IReadOnlyCollection<string> UnknownColumns,
    IReadOnlyCollection<ProductImportGroupProposal> ProposedGroups)
{
    public static ProductImportResponse Success(
        int total,
        int inserted,
        int updated,
        IReadOnlyCollection<string> unknownColumns,
        IReadOnlyCollection<ProductImportGroupProposal> proposedGroups) =>
        new(
            total,
            inserted,
            updated,
            WouldInsert: 0,
            ErrorCount: 0,
            DryRun: false,
            Skipped: false,
            ImmutableArray<ProductImportError>.Empty,
            unknownColumns,
            proposedGroups);

    public static ProductImportResponse DryRunResult(
        int total,
        int wouldInsert,
        IReadOnlyCollection<string> unknownColumns,
        IReadOnlyCollection<ProductImportGroupProposal> proposedGroups,
        int inserted = 0,
        int updated = 0) =>
        new(
            total,
            Inserted: inserted,
            Updated: updated,
            WouldInsert: wouldInsert,
            ErrorCount: 0,
            DryRun: true,
            Skipped: false,
            ImmutableArray<ProductImportError>.Empty,
            unknownColumns,
            proposedGroups);

    public static ProductImportResponse Failure(
        int total,
        IReadOnlyList<ProductImportError> errors,
        IReadOnlyCollection<string> unknownColumns,
        IReadOnlyCollection<ProductImportGroupProposal> proposedGroups)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return new(
            total,
            Inserted: 0,
            Updated: 0,
            WouldInsert: 0,
            errors.Count,
            DryRun: false,
            Skipped: false,
            errors,
            unknownColumns,
            proposedGroups);
    }

    public static ProductImportResponse SkippedResult() =>
        new(
            0,
            0,
            0,
            0,
            0,
            DryRun: false,
            Skipped: true,
            ImmutableArray<ProductImportError>.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<ProductImportGroupProposal>.Empty);
}
