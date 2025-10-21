using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace CineBoutique.Inventory.Infrastructure.Database.Products;

public sealed class ProductGroupRepository : IProductGroupRepository
{
    private readonly NpgsqlConnection _conn;

    public ProductGroupRepository(NpgsqlConnection conn) => _conn = conn ?? throw new ArgumentNullException(nameof(conn));

    public async Task<long?> EnsureGroupAsync(string? group, string? subGroup, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(group) && string.IsNullOrWhiteSpace(subGroup))
        {
            return null;
        }

        await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);

        long? parentId = null;
        if (!string.IsNullOrWhiteSpace(group))
        {
            parentId = await UpsertAsync(group!, null, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(subGroup))
        {
            return parentId;
        }

        return await UpsertAsync(subGroup!, parentId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> UpsertAsync(string label, long? parentId, CancellationToken ct)
    {
        var code = Slugify(label);
        const string sql = @"
    INSERT INTO ""ProductGroup"" (""Code"",""Label"",""ParentId"")
    VALUES (@code,@label,@pid)
    ON CONFLICT (""Code"") DO UPDATE SET ""Label"" = EXCLUDED.""Label""
    RETURNING ""Id"";";
        return await _conn.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            new { code, label, pid = (object?)parentId ?? DBNull.Value },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    [SuppressMessage("Globalization", "CA1308", Justification = "Slug normalization requires lowercase")]
    private static string Slugify(string s) =>
        System.Text.RegularExpressions.Regex.Replace(
            s.Normalize().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

    private async Task EnsureConnectionOpenAsync(CancellationToken ct)
    {
        if (_conn.State == ConnectionState.Closed)
        {
            await _conn.OpenAsync(ct).ConfigureAwait(false);
        }
    }
}
