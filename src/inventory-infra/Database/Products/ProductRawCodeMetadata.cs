using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace CineBoutique.Inventory.Infrastructure.Database.Products;

internal static class ProductRawCodeMetadata
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string? _projection;
    private static bool? _supportsRawCodeColumn;

    public static async Task<(string Projection, bool HasRawCodeColumn)> GetRawCodeProjectionAsync(
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_projection) && _supportsRawCodeColumn.HasValue)
        {
            return (_projection!, _supportsRawCodeColumn!.Value);
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(_projection) && _supportsRawCodeColumn.HasValue)
            {
                return (_projection!, _supportsRawCodeColumn!.Value);
            }

            const string sql = @"SELECT EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE LOWER(table_name) = LOWER(@Table)
      AND LOWER(column_name) = LOWER(@Column)
);";

            var hasColumn = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    sql,
                    new { Table = "Product", Column = "Code" },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            _supportsRawCodeColumn = hasColumn;
            _projection = hasColumn ? "\"Code\"" : "NULL::text";

            return (_projection!, hasColumn);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<bool> SupportsRawCodeColumnAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        var (_, hasColumn) = await GetRawCodeProjectionAsync(connection, cancellationToken).ConfigureAwait(false);
        return hasColumn;
    }
}
