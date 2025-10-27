using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace CineBoutique.Inventory.Infrastructure.Database.Inventory;

public sealed record OperatorColumnsState(bool HasOperatorDisplayName, bool OperatorDisplayNameIsNullable, bool HasOwnerUserId);

public sealed record OperatorSqlFragments(
    string Projection,
    string OwnerDisplayProjection,
    string OperatorDisplayProjection,
    string OwnerUserIdProjection,
    string? JoinClause);

public static class InventoryOperatorSqlHelper
{
    public static async Task<OperatorColumnsState> DetectOperatorColumnsAsync(
        IDbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var hasOperatorDisplayName = await InventoryDbUtilities
            .ColumnExistsAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken)
            .ConfigureAwait(false);

        var operatorDisplayNameIsNullable = hasOperatorDisplayName && await InventoryDbUtilities
            .ColumnIsNullableAsync(connection, "CountingRun", "OperatorDisplayName", cancellationToken)
            .ConfigureAwait(false);

        var hasOwnerUserId = await InventoryDbUtilities
            .ColumnExistsAsync(connection, "CountingRun", "OwnerUserId", cancellationToken)
            .ConfigureAwait(false);

        return new OperatorColumnsState(hasOperatorDisplayName, operatorDisplayNameIsNullable, hasOwnerUserId);
    }

    public static OperatorSqlFragments BuildOperatorSqlFragments(
        string runAlias,
        string ownerAlias,
        OperatorColumnsState state)
    {
        ArgumentNullException.ThrowIfNull(runAlias);
        ArgumentNullException.ThrowIfNull(ownerAlias);
        ArgumentNullException.ThrowIfNull(state);

        var ownerUserIdProjection = state.HasOwnerUserId
            ? $"{runAlias}.\"OwnerUserId\""
            : "NULL::uuid";

        var operatorDisplayProjection = state.HasOperatorDisplayName
            ? $"{runAlias}.\"OperatorDisplayName\""
            : "NULL::text";

        if (state.HasOwnerUserId)
        {
            var ownerDisplayProjection = $"{ownerAlias}.\"DisplayName\"";
            var joinClause = $"LEFT JOIN \"ShopUser\" {ownerAlias} ON {ownerAlias}.\"Id\" = {runAlias}.\"OwnerUserId\"";
            var projection = $"COALESCE({ownerDisplayProjection}, {operatorDisplayProjection})";
            return new OperatorSqlFragments(
                projection,
                ownerDisplayProjection,
                operatorDisplayProjection,
                ownerUserIdProjection,
                joinClause);
        }

        const string ownerDisplayFallback = "NULL::text";
        var defaultProjection = $"COALESCE({ownerDisplayFallback}, {operatorDisplayProjection})";
        return new OperatorSqlFragments(
            defaultProjection,
            ownerDisplayFallback,
            operatorDisplayProjection,
            ownerUserIdProjection,
            null);
    }

    public static string AppendJoinClause(string? joinClause) =>
        string.IsNullOrWhiteSpace(joinClause) ? string.Empty : $"\n{joinClause}";
}
