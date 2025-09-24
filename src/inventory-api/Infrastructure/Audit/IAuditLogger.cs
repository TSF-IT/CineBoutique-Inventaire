namespace CineBoutique.Inventory.Api.Infrastructure.Audit;

public interface IAuditLogger
{
    Task LogAsync(string? user, string message, string? action = null);
}
