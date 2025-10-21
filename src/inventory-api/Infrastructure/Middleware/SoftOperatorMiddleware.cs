using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CineBoutique.Inventory.Api.Infrastructure.Middleware;

public sealed class SoftOperatorMiddleware
{
    public const string OperatorContextItemKey = "SoftOperatorMiddleware.OperatorContext";

    private readonly RequestDelegate _next;
    private readonly ILogger<SoftOperatorMiddleware> _logger;

    public SoftOperatorMiddleware(RequestDelegate next, ILogger<SoftOperatorMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        try
        {
            if (context.Request.Headers.TryGetValue("X-Operator-Id", out var operatorIdValues))
            {
                var operatorIdText = operatorIdValues.ToString().Trim();
                if (Guid.TryParse(operatorIdText, out var operatorId))
                {
                    var operatorName = ExtractOptionalHeader(context, "X-Operator-Name");
                    var sessionId = ExtractOptionalHeader(context, "X-Session-Id");

                    context.Items[OperatorContextItemKey] = new OperatorContext(operatorId, operatorName, sessionId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Impossible de lire les en-têtes opérateur.");
        }

        await _next(context).ConfigureAwait(false);
    }

    private static string? ExtractOptionalHeader(HttpContext context, string headerName)
    {
        if (!context.Request.Headers.TryGetValue(headerName, out var values))
        {
            return null;
        }

        var candidate = values.ToString();
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate.Trim();
    }

}
