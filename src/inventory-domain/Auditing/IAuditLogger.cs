namespace CineBoutique.Inventory.Domain.Auditing;

public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken);
}
