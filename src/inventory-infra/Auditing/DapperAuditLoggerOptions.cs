namespace CineBoutique.Inventory.Infrastructure.Auditing;

public sealed class DapperAuditLoggerOptions
{
    public string? Schema { get; init; }
    public string? Table  { get; init; }
}
