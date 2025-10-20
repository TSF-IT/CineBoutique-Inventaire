using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Api.Infrastructure.Logging;

static partial class ApiLog
{
    [LoggerMessage(1001, LogLevel.Information, "Startup: API ready")]
    public static partial void ApiReady(ILogger logger);

    [LoggerMessage(2001, LogLevel.Debug, "Inventory search for {Query}")]
    public static partial void InventorySearch(ILogger logger, string query);
}
