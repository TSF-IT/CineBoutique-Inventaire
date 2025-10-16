using System.Collections.Immutable;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductImportResponse(int Inserted, IReadOnlyList<ProductImportError> Errors)
{
    public static ProductImportResponse Success(int inserted) =>
        new(inserted, ImmutableArray<ProductImportError>.Empty);

    public static ProductImportResponse Failure(IReadOnlyList<ProductImportError> errors) =>
        new(0, errors);
}
