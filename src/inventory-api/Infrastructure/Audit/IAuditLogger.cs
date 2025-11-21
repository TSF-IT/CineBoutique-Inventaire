namespace CineBoutique.Inventory.Api.Infrastructure.Audit
{
    public interface IAuditLogger
    {
        Task LogAsync(string message, string? actor = null, string? category = null, CancellationToken cancellationToken = default);
    }
}
