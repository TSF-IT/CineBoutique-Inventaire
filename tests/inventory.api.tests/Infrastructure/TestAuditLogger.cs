using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CineBoutique.Inventory.Api.Infrastructure.Audit;

namespace CineBoutique.Inventory.Api.Tests.Infrastructure;

public sealed class TestAuditLogger : IAuditLogger
{
    private readonly ConcurrentQueue<AuditLogEntry> _entries = new();

    public Task LogAsync(string message, string? actor = null, string? category = null, CancellationToken cancellationToken = default)
    {
        _entries.Enqueue(new AuditLogEntry(message, actor, category));
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<AuditLogEntry> ReadAll() => _entries.ToArray();

    public IReadOnlyList<AuditLogEntry> Drain()
    {
        var drained = new List<AuditLogEntry>();
        while (_entries.TryDequeue(out var entry))
        {
            drained.Add(entry);
        }

        return drained;
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
            // Intentionally left blank.
        }
    }
}

public sealed record AuditLogEntry(string Message, string? Actor, string? Category);
