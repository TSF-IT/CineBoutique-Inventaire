using System.Data;

namespace CineBoutique.Inventory.Api.Services.Products.Import;

public interface IProductImportHistoryStore
{
    Task InsertStartedAsync(
        Guid historyId,
        Guid shopId,
        DateTimeOffset startedAt,
        string? username,
        string fileSha256,
        IDbTransaction transaction,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid historyId,
        string status,
        int totalLines,
        int inserted,
        int errorCount,
        DateTimeOffset finishedAt,
        TimeSpan duration,
        IDbTransaction transaction,
        CancellationToken cancellationToken);
}
