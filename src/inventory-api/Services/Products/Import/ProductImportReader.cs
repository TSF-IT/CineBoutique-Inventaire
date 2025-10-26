using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Validation;

namespace CineBoutique.Inventory.Api.Services.Products.Import;

internal sealed class ProductImportReader : IProductImportReader
{
    private const long MaxCsvSizeBytes = 25L * 1024L * 1024L;

    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Dictionary<string, string> HeaderSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sku"] = KnownColumns.Sku,
        ["item"] = KnownColumns.Sku,
        ["code"] = KnownColumns.Sku,
        ["ean"] = KnownColumns.Ean,
        ["ean13"] = KnownColumns.Ean,
        ["barcode"] = KnownColumns.Ean,
        ["barcode_rfid"] = KnownColumns.Ean,
        ["rfid"] = KnownColumns.Ean,
        ["name"] = KnownColumns.Name,
        ["descr"] = KnownColumns.Name,
        ["description"] = KnownColumns.Name,
        ["libelle"] = KnownColumns.Name,
        ["groupe"] = KnownColumns.Group,
        ["group"] = KnownColumns.Group,
        ["sous_groupe"] = KnownColumns.SubGroup,
        ["subgroup"] = KnownColumns.SubGroup,
        ["sousgroupe"] = KnownColumns.SubGroup,
        ["sousGroupe"] = KnownColumns.SubGroup
    };

    private static readonly HashSet<string> KnownColumnNames = new(StringComparer.OrdinalIgnoreCase)
    {
        KnownColumns.Sku,
        KnownColumns.Ean,
        KnownColumns.Name,
        KnownColumns.Group,
        KnownColumns.SubGroup
    };

    public async Task<ProductImportBuffer> BufferAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (source.CanSeek)
        {
            source.Position = 0;
        }

        var destination = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long totalBytes = 0;

        try
        {
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, bytesRead);
                totalBytes += bytesRead;

                if (totalBytes > MaxCsvSizeBytes)
                {
                    throw new ProductImportPayloadTooLargeException(MaxCsvSizeBytes);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        destination.Position = 0;
        var sha256 = Convert.ToHexString(hash.GetHashAndReset());
        var encoding = DetectEncoding(destination);
        destination.Position = 0;

        return new ProductImportBuffer(destination, sha256, encoding);
    }

    public async Task<ProductCsvParseOutcome> ParseAsync(ProductImportBuffer buffer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        buffer.Stream.Position = 0;

        var rows = new List<ProductCsvRow>();
        var errors = new List<ProductImportError>();
        var skippedLines = new List<ProductImportSkippedLine>();
        var unknownColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var headerCaptured = false;
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var proposedGroups = new List<ProductImportGroupProposal>();
        var proposedGroupsSet = new HashSet<ProductImportGroupKey>(ProductImportGroupKeyComparer.Instance);

        var lineNumber = 0;
        var totalLines = 0;
        List<string>? headers = null;

        using var reader = new StreamReader(
            buffer.Stream,
            buffer.Encoding,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } rawLine)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lineNumber++;
            var line = rawLine.TrimEnd('\r');

            if (headers is null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var headerFields = ParseFields(line);
                headers = headerFields.ToList();

                headerMap.Clear();
                for (var i = 0; i < headers.Count; i++)
                {
                    var raw = headers[i];
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    var key = HeaderSynonyms.TryGetValue(raw, out var mapped) ? mapped : raw;
                    if (!headerMap.ContainsKey(key))
                    {
                        headerMap[key] = i;
                    }
                }

                if (!headerMap.ContainsKey(KnownColumns.Sku))
                {
                    errors.Add(new ProductImportError(lineNumber, "MISSING_SKU_COLUMN"));
                }

                if (!headerCaptured)
                {
                    foreach (var key in headerMap.Keys)
                    {
                        if (!KnownColumnNames.Contains(key))
                        {
                            unknownColumns.Add(key);
                        }
                    }

                    headerCaptured = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            totalLines++;

            var fields = ParseFields(line);
            if (fields.Count > headers.Count)
            {
                errors.Add(new ProductImportError(lineNumber, "INVALID_COLUMN_COUNT"));
                continue;
            }

            if (fields.Count < headers.Count)
            {
                while (fields.Count < headers.Count)
                {
                    fields.Add(string.Empty);
                }
            }

            string? sku = null;
            string? ean = null;
            string? name = null;
            string? group = null;
            string? subGroup = null;

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var need in new[]
                   {
                       KnownColumns.Sku,
                       KnownColumns.Ean,
                       KnownColumns.Name,
                       KnownColumns.Group,
                       KnownColumns.SubGroup
                   })
            {
                if (!headerMap.TryGetValue(need, out var idx))
                {
                    continue;
                }

                string value;
                try
                {
                    value = fields[idx] ?? string.Empty;
                }
                catch
                {
                    value = string.Empty;
                }

                row[need] = value.Trim();
            }

            if (row.TryGetValue(KnownColumns.Sku, out var skuValue) && !string.IsNullOrWhiteSpace(skuValue))
            {
                sku ??= skuValue;
            }

            if (row.TryGetValue(KnownColumns.Ean, out var eanValue))
            {
                ean = string.IsNullOrWhiteSpace(eanValue) ? null : eanValue;
            }

            if (row.TryGetValue(KnownColumns.Name, out var nameValue) && !string.IsNullOrWhiteSpace(nameValue))
            {
                name = nameValue;
            }

            if (row.TryGetValue(KnownColumns.Group, out var groupValue))
            {
                group = ProductImportFieldNormalizer.NormalizeOptional(groupValue);
            }

            if (row.TryGetValue(KnownColumns.SubGroup, out var subGroupValue))
            {
                subGroup = ProductImportFieldNormalizer.NormalizeOptional(subGroupValue);
            }

            var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in unknownColumns)
            {
                if (!headerMap.TryGetValue(key, out var idx))
                {
                    continue;
                }

                string value;
                try
                {
                    value = fields[idx] ?? string.Empty;
                }
                catch
                {
                    value = string.Empty;
                }

                value = value.Trim();
                attributes[key] = string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (string.IsNullOrWhiteSpace(sku))
            {
                errors.Add(new ProductImportError(lineNumber, "EMPTY_SKU"));
                continue;
            }

            name = string.IsNullOrWhiteSpace(name) ? sku : name;

            var normalizedGroup = ProductImportFieldNormalizer.NormalizeOptional(group);
            var normalizedSubGroup = ProductImportFieldNormalizer.NormalizeOptional(subGroup);

            if (normalizedGroup is not null || normalizedSubGroup is not null)
            {
                var key = new ProductImportGroupKey(normalizedGroup, normalizedSubGroup);
                if (proposedGroupsSet.Add(key))
                {
                    proposedGroups.Add(new ProductImportGroupProposal(normalizedGroup, normalizedSubGroup));
                }
            }

            rows.Add(new ProductCsvRow(
                sku,
                name!,
                ean,
                normalizedGroup,
                normalizedSubGroup,
                attributes,
                lineNumber,
                line));
        }

        if (headers is null && errors.Count == 0)
        {
            errors.Add(new ProductImportError(0, "MISSING_HEADER"));
        }

        var unknownColumnsImmutable = unknownColumns.Count == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.CreateRange(
                unknownColumns.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase));

        var proposedGroupsImmutable = proposedGroups.Count == 0
            ? ImmutableArray<ProductImportGroupProposal>.Empty
            : proposedGroups.ToImmutableArray();

        var skippedLinesImmutable = skippedLines.Count == 0
            ? ImmutableArray<ProductImportSkippedLine>.Empty
            : skippedLines.ToImmutableArray();

        var duplicateReport = BuildDuplicateReport(rows);

        return new ProductCsvParseOutcome(
            rows,
            errors,
            totalLines,
            unknownColumnsImmutable,
            proposedGroupsImmutable,
            skippedLinesImmutable,
            duplicateReport);
    }

    private static Encoding DetectEncoding(MemoryStream stream)
    {
        if (!stream.TryGetBuffer(out var segment))
        {
            var snapshot = stream.ToArray();
            return DetectEncoding(snapshot.AsSpan());
        }

        return DetectEncoding(segment.AsSpan());
    }

    private static Encoding DetectEncoding(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        try
        {
            StrictUtf8.GetString(buffer);
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1;
        }
    }

    private static ProductImportDuplicateReport BuildDuplicateReport(IReadOnlyList<ProductCsvRow> rows)
    {
        if (rows.Count == 0)
        {
            return ProductImportDuplicateReport.Empty;
        }

        var duplicateSkus = rows
            .GroupBy(row => row.Sku, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => ProductImportDuplicateEntry.Create(
                group.Key,
                group.Select(row => row.LineNumber).OrderBy(static n => n),
                group.Select(row => row.RawLine)))
            .OrderBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duplicateEans = rows
            .Select(row => new
            {
                Row = row,
                Key = NormalizeEanForDuplicates(row.Ean)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group =>
            {
                var displayValue = group.First().Row.Ean ?? group.Key;
                return ProductImportDuplicateEntry.Create(
                    displayValue,
                    group.Select(x => x.Row.LineNumber).OrderBy(static n => n),
                    group.Select(x => x.Row.RawLine));
            })
            .OrderBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (duplicateSkus.Count == 0 && duplicateEans.Count == 0)
        {
            return ProductImportDuplicateReport.Empty;
        }

        return ProductImportDuplicateReport.Create(duplicateSkus, duplicateEans);
    }

    private static string? NormalizeEanForDuplicates(string? ean)
    {
        if (string.IsNullOrWhiteSpace(ean))
        {
            return null;
        }

        var normalized = InventoryCodeValidator.Normalize(ean);
        if (normalized is not null)
        {
            return normalized;
        }

        var digits = ProductImportFieldNormalizer.DigitsOnlyRegex.Replace(ean, string.Empty);
        if (digits.Length > 0)
        {
            return digits;
        }

        return ean.Trim();
    }

    private static List<string> ParseFields(string line)
    {
        var result = new List<string>();
        var builder = new StringBuilder(line.Length);
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (current == '\"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                {
                    builder.Append('\"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (current == ';' && !inQuotes)
            {
                result.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        result.Add(builder.ToString());
        return result;
    }

    private static class KnownColumns
    {
        public const string Sku = "sku";
        public const string Ean = "ean";
        public const string Name = "name";
        public const string Group = "groupe";
        public const string SubGroup = "sousGroupe";
    }
}
