using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using CineBoutique.Inventory.Api.Models;

namespace CineBoutique.Inventory.Api.Services.Products.Import;

public sealed class ProductImportBuffer : IDisposable
{
    public ProductImportBuffer(MemoryStream stream, string sha256, Encoding encoding)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        Sha256 = sha256 ?? throw new ArgumentNullException(nameof(sha256));
        Encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
    }

    public MemoryStream Stream { get; }

    public string Sha256 { get; }

    public Encoding Encoding { get; }

    public void Dispose() => Stream.Dispose();
}

public sealed record ProductCsvRow(
    string Sku,
    string Name,
    string? Ean,
    string? Group,
    string? SubGroup,
    IReadOnlyDictionary<string, object?> Attributes,
    int LineNumber,
    string RawLine);

public sealed record ProductCsvParseOutcome(
    IReadOnlyList<ProductCsvRow> Rows,
    IReadOnlyList<ProductImportError> Errors,
    int TotalLines,
    ImmutableArray<string> UnknownColumns,
    ImmutableArray<ProductImportGroupProposal> ProposedGroups,
    ImmutableArray<ProductImportSkippedLine> SkippedLines,
    ProductImportDuplicateReport Duplicates)
{
    public static ProductCsvParseOutcome CreateEmptyFile()
    {
        return new ProductCsvParseOutcome(
            Array.Empty<ProductCsvRow>(),
            new[] { new ProductImportError(0, "EMPTY_FILE") },
            0,
            ImmutableArray<string>.Empty,
            ImmutableArray<ProductImportGroupProposal>.Empty,
            ImmutableArray<ProductImportSkippedLine>.Empty,
            ProductImportDuplicateReport.Empty);
    }
}

public sealed record ProductImportWriteStatistics(int Created, int Updated)
{
    public static ProductImportWriteStatistics Empty { get; } = new(0, 0);
}

public sealed record ProductImportValidationResult(bool IsValid, IReadOnlyList<ProductImportError> Errors)
{
    public static ProductImportValidationResult Success { get; } =
        new(true, Array.Empty<ProductImportError>());

    public static ProductImportValidationResult Failure(IReadOnlyList<ProductImportError> errors) =>
        new(false, errors);
}

internal sealed record ProductImportGroupKey(string? Group, string? SubGroup);

internal sealed class ProductImportGroupKeyComparer : IEqualityComparer<ProductImportGroupKey>
{
    public static ProductImportGroupKeyComparer Instance { get; } = new();

    public bool Equals(ProductImportGroupKey? x, ProductImportGroupKey? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        return string.Equals(x.Group, y.Group, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.SubGroup, y.SubGroup, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(ProductImportGroupKey obj)
    {
        var groupHash = obj.Group is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Group);
        var subGroupHash = obj.SubGroup is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SubGroup);
        return HashCode.Combine(groupHash, subGroupHash);
    }
}
