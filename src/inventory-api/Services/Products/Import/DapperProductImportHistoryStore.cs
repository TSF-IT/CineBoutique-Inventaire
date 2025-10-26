using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace CineBoutique.Inventory.Api.Services.Products.Import;

internal sealed class DapperProductImportHistoryStore : IProductImportHistoryStore
{
    public Task InsertStartedAsync(
        Guid historyId,
        Guid shopId,
        DateTimeOffset startedAt,
        string? username,
        string fileSha256,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction not bound to a connection.");

        const string sql = """
INSERT INTO "ProductImportHistory" ("Id", "ShopId", "StartedAt", "Username", "FileSha256", "TotalLines", "Inserted", "ErrorCount", "Status")
VALUES (@Id, @ShopId, @StartedAt, @Username, @FileSha256, 0, 0, 0, @Status);
""";

        var parameters = new
        {
            Id = historyId,
            ShopId = shopId,
            StartedAt = startedAt,
            Username = username,
            FileSha256 = fileSha256,
            Status = ProductImportHistoryStatuses.Started
        };

        var command = new CommandDefinition(
            sql,
            parameters,
            transaction: transaction,
            cancellationToken: cancellationToken);

        return connection.ExecuteAsync(command);
    }

    public Task CompleteAsync(
        Guid historyId,
        string status,
        int totalLines,
        int inserted,
        int errorCount,
        DateTimeOffset finishedAt,
        TimeSpan duration,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction not bound to a connection.");

        const string sql = """
UPDATE "ProductImportHistory"
SET "FinishedAt" = @FinishedAt,
    "DurationMs" = @DurationMs,
    "Status" = @Status,
    "TotalLines" = @TotalLines,
    "Inserted" = @Inserted,
    "ErrorCount" = @ErrorCount
WHERE "Id" = @Id;
""";

        var durationMs = (int)Math.Min(int.MaxValue, Math.Round(duration.TotalMilliseconds));

        var parameters = new
        {
            Id = historyId,
            FinishedAt = finishedAt,
            DurationMs = durationMs,
            Status = status,
            TotalLines = totalLines,
            Inserted = inserted,
            ErrorCount = errorCount
        };

        var command = new CommandDefinition(
            sql,
            parameters,
            transaction: transaction,
            cancellationToken: cancellationToken);

        return connection.ExecuteAsync(command);
    }
}
