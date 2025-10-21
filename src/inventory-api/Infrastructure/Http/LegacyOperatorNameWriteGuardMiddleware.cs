using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace CineBoutique.Inventory.Api.Infrastructure.Http;

public sealed class LegacyOperatorNameWriteGuardMiddleware
{
    private static readonly string[] GuardedMethods = [HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch];
    private readonly RequestDelegate _next;
    private readonly ILogger<LegacyOperatorNameWriteGuardMiddleware> _logger;

    public LegacyOperatorNameWriteGuardMiddleware(RequestDelegate next, ILogger<LegacyOperatorNameWriteGuardMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);

        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        if (ShouldInspect(request) && await ContainsLegacyOperatorNameAsync(request).ConfigureAwait(false))
        {
            _logger.LogInformation("Rejecting legacy 'operatorName' field on {Path}", request.Path);

            var problem = new ProblemDetails
            {
                Title = "Legacy field not allowed",
                Detail = "Field \"operatorName\" is no longer accepted. Use \"ownerUserId\".",
                Status = StatusCodes.Status400BadRequest,
                Type = "about:blank"
            };

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(problem).ConfigureAwait(false);
            return;
        }

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private static bool ShouldInspect(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!GuardedMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!request.Path.StartsWithSegments("/api/inventories", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HasJsonContentType(request);
    }

    private static async Task<bool> ContainsLegacyOperatorNameAsync(HttpRequest request)
    {
        request.EnableBuffering();

        if (!request.Body.CanSeek)
        {
            return false;
        }

        request.Body.Position = 0;

        string payload;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "operatorName", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool HasJsonContentType(HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContentType))
        {
            return false;
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType))
        {
            return false;
        }

        var mediaTypeValue = mediaType.MediaType.Value;
        if (!string.IsNullOrEmpty(mediaTypeValue) && mediaTypeValue.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (mediaType.Suffix.HasValue && mediaType.Suffix.Value.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
