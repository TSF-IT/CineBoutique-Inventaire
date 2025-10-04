using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CineBoutique.Inventory.Api.Infrastructure.Middleware;

public sealed class LegacyOperatorGuardMiddleware
{
    private readonly RequestDelegate _next;

    public LegacyOperatorGuardMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (ShouldInspect(context.Request))
        {
            if (await ContainsLegacyOperatorAsync(context.Request).ConfigureAwait(false))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response
                    .WriteAsJsonAsync(new { error = "operatorName is no longer accepted. Use ownerUserId." })
                    .ConfigureAwait(false);
                return;
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool ShouldInspect(HttpRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        if (!request.Path.StartsWithSegments("/api/inventories", out var remaining))
        {
            return false;
        }

        if (!remaining.StartsWithSegments("/runs", out remaining))
        {
            return false;
        }

        var tail = remaining.Value ?? string.Empty;

        return tail.StartsWith("/start", StringComparison.OrdinalIgnoreCase) ||
               tail.StartsWith("/complete", StringComparison.OrdinalIgnoreCase) ||
               tail.StartsWith("/release", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ContainsLegacyOperatorAsync(HttpRequest request)
    {
        request.EnableBuffering();

        if (!request.Body.CanSeek)
        {
            return false;
        }

        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var payload = await reader.ReadToEndAsync().ConfigureAwait(false);
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { AllowTrailingCommas = true });
            return ContainsLegacyOperator(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsLegacyOperator(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (IsLegacyProperty(property.Name))
                    {
                        return true;
                    }

                    if (ContainsLegacyOperator(property.Value))
                    {
                        return true;
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsLegacyOperator(item))
                    {
                        return true;
                    }
                }
                break;
        }

        return false;
    }

    private static bool IsLegacyProperty(string propertyName)
    {
        return string.Equals(propertyName, "operator", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(propertyName, "operatorName", StringComparison.OrdinalIgnoreCase);
    }
}
