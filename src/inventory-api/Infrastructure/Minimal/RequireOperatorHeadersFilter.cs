using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CineBoutique.Inventory.Api.Infrastructure.Minimal;

internal sealed class RequireOperatorHeadersFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!HasNonEmptyHeader(context.HttpContext.Request.Headers, "X-Operator-Id") ||
            !HasNonEmptyHeader(context.HttpContext.Request.Headers, "X-Operator-Name"))
        {
            var problem = Results.Problem(
                title: "missing_operator_headers",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "X-Operator-Id and X-Operator-Name are required.");

            return ValueTask.FromResult<object?>(problem);
        }

        return next(context);
    }

    private static bool HasNonEmptyHeader(IHeaderDictionary headers, string headerName)
    {
        if (!headers.TryGetValue(headerName, out var values))
        {
            return false;
        }

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return false;
    }
}
