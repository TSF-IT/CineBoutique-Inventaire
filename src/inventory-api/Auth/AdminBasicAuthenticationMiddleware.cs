using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CineBoutique.Inventory.Api.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace CineBoutique.Inventory.Api.Auth;

public sealed class AdminBasicAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<AdminAuthOptions> _options;
    private readonly ILogger<AdminBasicAuthenticationMiddleware> _logger;

    public AdminBasicAuthenticationMiddleware(
        RequestDelegate next,
        IOptionsMonitor<AdminAuthOptions> options,
        ILogger<AdminBasicAuthenticationMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.UserName) || string.IsNullOrWhiteSpace(options.Password))
        {
            _logger.LogError("Les identifiants admin ne sont pas configurés.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Admin credentials are not configured." }).ConfigureAwait(false);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderNames.Authorization, out var authorizationHeader))
        {
            await ChallengeAsync(context).ConfigureAwait(false);
            return;
        }

        var header = authorizationHeader.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            await ChallengeAsync(context).ConfigureAwait(false);
            return;
        }

        var token = header[6..].Trim();
        string decoded;
        try
        {
            var bytes = Convert.FromBase64String(token);
            decoded = Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Entête Basic invalide.");
            await ChallengeAsync(context).ConfigureAwait(false);
            return;
        }

        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex <= 0)
        {
            await ChallengeAsync(context).ConfigureAwait(false);
            return;
        }

        var username = decoded[..separatorIndex];
        var password = decoded[(separatorIndex + 1)..];

        if (!AreEquals(username, options.UserName) || !AreEquals(password, options.Password))
        {
            _logger.LogWarning("Tentative de connexion admin échouée pour l'utilisateur {User}", username);
            await ChallengeAsync(context).ConfigureAwait(false);
            return;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, options.UserName),
            new(ClaimTypes.Role, "Admin"),
        };

        var identity = new ClaimsIdentity(claims, authenticationType: "Basic");
        context.User = new ClaimsPrincipal(identity);

        await _next(context).ConfigureAwait(false);
    }

    private static bool AreEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static Task ChallengeAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers[HeaderNames.WWWAuthenticate] = "Basic realm=\"Admin\"";
        return context.Response.WriteAsync("Unauthorized");
    }
}
