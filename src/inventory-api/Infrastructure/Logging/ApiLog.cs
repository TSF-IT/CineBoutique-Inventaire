using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Api.Infrastructure.Logging;

static partial class ApiLog
{
    [LoggerMessage(1001, LogLevel.Warning, "Domain audit fallback: {Reason}")]
    public static partial void DomainAuditFallback(ILogger logger, string reason);

    [LoggerMessage(1002, LogLevel.Warning, "DB audit write failed")]
    public static partial void DbAuditWriteFailed(ILogger logger, Exception ex);

    [LoggerMessage(1003, LogLevel.Information, "Import step: {Step}")]
    public static partial void ImportStep(ILogger logger, string step);

    [LoggerMessage(1004, LogLevel.Information, "Startup: API ready")]
    public static partial void ApiReady(ILogger logger);

    [LoggerMessage(1005, LogLevel.Debug, "Inventory search for {Query}")]
    public static partial void InventorySearch(ILogger logger, string query);
}
