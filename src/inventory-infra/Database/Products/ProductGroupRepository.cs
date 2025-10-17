using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Infrastructure.Database;
using Dapper;
using Npgsql;

namespace CineBoutique.Inventory.Infrastructure.Database.Products;

public sealed class ProductGroupRepository : IProductGroupRepository
{
    private const string SelectGroupCodeSql = "SELECT \"Code\" FROM \"ProductGroup\" WHERE \"Id\" = @Id;";
    private const string SelectGroupIdByCodeSql = "SELECT \"Id\" FROM \"ProductGroup\" WHERE \"Code\" IS NOT DISTINCT FROM @Code LIMIT 1;";
    private const string UpdateGroupSql = "UPDATE \"ProductGroup\" SET \"Label\" = @Label, \"ParentId\" = @ParentId WHERE \"Id\" = @Id;";
    private const string InsertGroupSql = "INSERT INTO \"ProductGroup\" (\"Code\", \"Label\", \"ParentId\") VALUES (@Code, @Label, @ParentId) RETURNING \"Id\";";
    private const string ProductGroupCodeUniqueConstraint = "uq_productgroup_code";

    private static readonly string UpsertSql = $"""
INSERT INTO "ProductGroup" ("Code", "Label", "ParentId")
VALUES (@Code, @Label, @ParentId)
ON CONFLICT ON CONSTRAINT "{ProductGroupCodeUniqueConstraint}"
DO UPDATE SET "Label" = EXCLUDED."Label"
RETURNING "Id";
""";

    private readonly IDbConnectionFactory _connectionFactory;

    public ProductGroupRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<long?> EnsureGroupAsync(string? group, string? subGroup, CancellationToken cancellationToken)
    {
        var parentLabel = NormalizeLabel(group);
        var childLabel = NormalizeLabel(subGroup);

        if (parentLabel is null && childLabel is null)
        {
            return null;
        }

        using var connection = _connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        string? parentCode = null;
        long? parentId = null;

        if (parentLabel is not null)
        {
            parentCode = GenerateSlug(parentLabel);
            parentId = await UpsertAsyncCore(connection, parentLabel, parentCode, null, cancellationToken).ConfigureAwait(false);
        }

        if (childLabel is not null)
        {
            if (parentId is null)
            {
                var fallbackCode = GenerateSlug(childLabel);
                return await UpsertAsyncCore(connection, childLabel, fallbackCode, null, cancellationToken).ConfigureAwait(false);
            }

            var childCode = GenerateSlug(childLabel, parentCode);
            return await UpsertAsyncCore(connection, childLabel, childCode, parentId, cancellationToken).ConfigureAwait(false);
        }

        return parentId;
    }

    internal async Task<long> UpsertAsync(string label, long? parentId, CancellationToken cancellationToken)
    {
        var normalizedLabel = NormalizeLabel(label) ?? throw new ArgumentException("Le libellé ne peut pas être vide.", nameof(label));

        using var connection = _connectionFactory.CreateConnection();
        await EnsureConnectionOpenAsync(connection, cancellationToken).ConfigureAwait(false);

        string? parentCode = null;
        if (parentId.HasValue)
        {
            parentCode = await connection.ExecuteScalarAsync<string?>(
                new CommandDefinition(SelectGroupCodeSql, new { Id = parentId.Value }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        var code = GenerateSlug(normalizedLabel, parentCode);
        return await UpsertAsyncCore(connection, normalizedLabel, code, parentId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureConnectionOpenAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        switch (connection)
        {
            case DbConnection dbConnection when dbConnection.State != ConnectionState.Open:
                await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                break;
            case { State: ConnectionState.Closed }:
                connection.Open();
                break;
        }
    }

    private static async Task<long> UpsertAsyncCore(IDbConnection connection, string label, string? code, long? parentId, CancellationToken cancellationToken)
    {
        var normalizedCode = string.IsNullOrEmpty(code) ? null : code;

        var parameters = new
        {
            Code = normalizedCode,
            Label = label,
            ParentId = parentId
        };

        try
        {
            return await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition(UpsertSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, "42P10", StringComparison.Ordinal))
        {
            return await UpsertWithManualConflictResolutionAsync(connection, label, normalizedCode, parentId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<long> UpsertWithManualConflictResolutionAsync(
        IDbConnection connection,
        string label,
        string? normalizedCode,
        long? parentId,
        CancellationToken cancellationToken)
    {
        var existingId = await connection.ExecuteScalarAsync<long?>(
            new CommandDefinition(
                SelectGroupIdByCodeSql,
                new { Code = normalizedCode },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (existingId.HasValue)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    UpdateGroupSql,
                    new { Label = label, ParentId = parentId, Id = existingId.Value },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            return existingId.Value;
        }

        try
        {
            return await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    InsertGroupSql,
                    new { Code = normalizedCode, Label = label, ParentId = parentId },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal))
        {
            var resolvedId = await connection.ExecuteScalarAsync<long?>(
                new CommandDefinition(
                    SelectGroupIdByCodeSql,
                    new { Code = normalizedCode },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (!resolvedId.HasValue)
            {
                throw;
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    UpdateGroupSql,
                    new { Label = label, ParentId = parentId, Id = resolvedId.Value },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            return resolvedId.Value;
        }
    }

    private static string? NormalizeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? GenerateSlug(string label, string? prefix = null)
    {
        var baseSlug = Slugify(label);
        if (string.IsNullOrEmpty(prefix))
        {
            return baseSlug;
        }

        if (string.IsNullOrEmpty(baseSlug))
        {
            return prefix;
        }

        return string.Create(prefix.Length + 1 + baseSlug.Length, (prefix, baseSlug), static (span, tuple) =>
        {
            var (pref, slug) = tuple;
            pref.AsSpan().CopyTo(span);
            span[pref.Length] = '-';
            slug.AsSpan().CopyTo(span[(pref.Length + 1)..]);
        });
    }

    private static string Slugify(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var normalized = trimmed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        var ascii = builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        builder.Clear();

        var previousWasDash = false;
        foreach (var character in ascii)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasDash = false;
            }
            else if (character is ' ' or '-' or '_' or '/' or '.')
            {
                if (!previousWasDash)
                {
                    builder.Append('-');
                    previousWasDash = true;
                }
            }
        }

        return builder.ToString().Trim('-');
    }
}
