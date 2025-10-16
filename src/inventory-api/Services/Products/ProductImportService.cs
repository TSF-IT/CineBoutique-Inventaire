using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Models;
using CineBoutique.Inventory.Api.Infrastructure.Time;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CineBoutique.Inventory.Api.Services.Products;

public sealed class ProductImportService : IProductImportService
{
    private const string CsvHeaderBarcode = "barcode_rfid";
    private const string CsvHeaderItem = "item";
    private const string CsvHeaderDescription = "descr";

    private static readonly Regex DigitsOnlyRegex = new("[^0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IDbConnection _connection;
    private readonly IClock _clock;
    private readonly ILogger<ProductImportService> _logger;

    public ProductImportService(IDbConnection connection, IClock clock, ILogger<ProductImportService> logger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProductImportResponse> ImportAsync(Stream csvStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csvStream);

        await using var bufferedStream = await BufferStreamAsync(csvStream, cancellationToken).ConfigureAwait(false);
        if (bufferedStream.Length == 0)
        {
            return ProductImportResponse.Failure(new[] { new ProductImportError(0, "EMPTY_FILE") });
        }

        var encoding = DetectEncoding(bufferedStream);

        if (_connection is not NpgsqlConnection npgsqlConnection)
        {
            throw new InvalidOperationException("L'import produit requiert une connexion Npgsql active.");
        }

        await EnsureConnectionOpenAsync(npgsqlConnection, cancellationToken).ConfigureAwait(false);

        await using var transaction = await npgsqlConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await TruncateProductsAsync(transaction, cancellationToken).ConfigureAwait(false);

            bufferedStream.Position = 0;
            using var reader = new StreamReader(bufferedStream, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

            var parseOutcome = await ParseAsync(reader, cancellationToken).ConfigureAwait(false);
            if (parseOutcome.Errors.Count > 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return ProductImportResponse.Failure(parseOutcome.Errors);
            }

            var inserted = await InsertRowsAsync(parseOutcome.Rows, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Import produits terminé : {Inserted} lignes insérées.", inserted);

            return ProductImportResponse.Success(inserted);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<MemoryStream> BufferStreamAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source is MemoryStream memoryStream && memoryStream.CanSeek)
        {
            memoryStream.Position = 0;
            return memoryStream;
        }

        var destination = new MemoryStream();
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        destination.Position = 0;
        return destination;
    }

    private static Encoding DetectEncoding(MemoryStream stream)
    {
        if (!stream.TryGetBuffer(out var bufferSegment))
        {
            var snapshot = stream.ToArray();
            return DetectEncoding(snapshot.AsSpan());
        }

        return DetectEncoding(bufferSegment.AsSpan());
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

    private async Task<int> InsertRowsAsync(IReadOnlyList<ProductCsvRow> rows, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        const string sql =
            "INSERT INTO \"Product\" (\"Sku\", \"Name\", \"Ean\", \"CodeDigits\", \"CreatedAtUtc\") " +
            "VALUES (@Sku, @Name, @Ean, @CodeDigits, @CreatedAtUtc);";

        var now = _clock.UtcNow;
        foreach (var row in rows)
        {
            var ean = NormalizeEan(row.Code);
            var parameters = new
            {
                row.Sku,
                row.Name,
                Ean = ean,
                row.CodeDigits,
                CreatedAtUtc = now
            };

            await _connection.ExecuteAsync(
                    new CommandDefinition(sql, parameters, transaction: transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        return rows.Count;
    }

    private string? NormalizeEan(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (code.Length <= 13)
        {
            return code;
        }

        _logger.LogDebug("Code importé trop long ({Length} > 13), colonne EAN laissée vide.", code.Length);
        return null;
    }

    private async Task TruncateProductsAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        const string truncateSql = "TRUNCATE TABLE \"Product\" RESTART IDENTITY CASCADE;";
        await _connection.ExecuteAsync(
                new CommandDefinition(truncateSql, transaction: transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task<ProductCsvParseOutcome> ParseAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<ProductCsvRow>();
        var errors = new List<ProductImportError>();
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lineNumber = 0;
        var headerProcessed = false;

        while (await reader.ReadLineAsync() is { } rawLine)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lineNumber++;
            var line = rawLine.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseFields(line);
            if (!headerProcessed)
            {
                headerProcessed = true;
                if (!IsValidHeader(fields))
                {
                    errors.Add(new ProductImportError(lineNumber, "INVALID_HEADER"));
                    break;
                }

                continue;
            }

            if (fields.Count != 3)
            {
                errors.Add(new ProductImportError(lineNumber, "INVALID_COLUMN_COUNT"));
                continue;
            }

            var codeRaw = fields[0].Trim();
            var sku = fields[1].Trim();
            var name = fields[2].Trim();

            if (string.IsNullOrWhiteSpace(sku))
            {
                errors.Add(new ProductImportError(lineNumber, "EMPTY_SKU"));
                continue;
            }

            if (!seenSkus.Add(sku))
            {
                errors.Add(new ProductImportError(lineNumber, "DUP_SKU_IN_FILE"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ProductImportError(lineNumber, "EMPTY_NAME"));
                continue;
            }

            var code = string.IsNullOrWhiteSpace(codeRaw) ? null : codeRaw;
            var digits = DigitsOnlyRegex.Replace(code ?? string.Empty, string.Empty);
            var codeDigits = string.IsNullOrEmpty(digits) ? null : digits;

            rows.Add(new ProductCsvRow(sku, name, code, codeDigits));
        }

        if (!headerProcessed)
        {
            errors.Add(new ProductImportError(0, "MISSING_HEADER"));
        }

        return new ProductCsvParseOutcome(rows, errors);
    }

    private static List<string> ParseFields(string line)
    {
        var result = new List<string>();
        var builder = new StringBuilder(line.Length);
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (current == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
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

    private static bool IsValidHeader(IReadOnlyList<string> columns)
    {
        if (columns.Count < 3)
        {
            return false;
        }

        return string.Equals(columns[0].Trim(), CsvHeaderBarcode, StringComparison.OrdinalIgnoreCase)
               && string.Equals(columns[1].Trim(), CsvHeaderItem, StringComparison.OrdinalIgnoreCase)
               && string.Equals(columns[2].Trim(), CsvHeaderDescription, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EnsureConnectionOpenAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.FullState.HasFlag(System.Data.ConnectionState.Open))
        {
            return;
        }

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record ProductCsvRow(string Sku, string Name, string? Code, string? CodeDigits);

    private sealed record ProductCsvParseOutcome(IReadOnlyList<ProductCsvRow> Rows, IReadOnlyList<ProductImportError> Errors);
}
