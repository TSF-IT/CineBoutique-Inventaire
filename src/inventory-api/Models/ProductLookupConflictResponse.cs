using System.ComponentModel.DataAnnotations;

namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductLookupConflictMatch(
    [property: Required] string Sku,
    [property: Required] string Code);

public sealed record ProductLookupConflictResponse(
    [property: Required] bool Ambiguous,
    [property: Required] string Code,
    [property: Required] string Digits,
    [property: Required] IReadOnlyList<ProductLookupConflictMatch> Matches);
