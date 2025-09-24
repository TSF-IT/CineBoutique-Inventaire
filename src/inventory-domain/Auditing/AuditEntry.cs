namespace CineBoutique.Inventory.Domain.Auditing;

public sealed record AuditEntry(
    string EntityName,
    string EntityId,
    string EventType,
    object? Payload,
    DateTimeOffset CreatedAtUtc);
