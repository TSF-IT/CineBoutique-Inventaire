namespace CineBoutique.Inventory.Api.Models;

public sealed record ProductImportSkippedLine(
    int Line,
    string Raw,
    string Reason);
