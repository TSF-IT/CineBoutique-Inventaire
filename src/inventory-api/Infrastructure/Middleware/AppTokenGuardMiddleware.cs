namespace CineBoutique.Inventory.Api.Infrastructure.Middleware
{
    public sealed class AppTokenGuardMiddleware
    {
        private const string HeaderName = "X-App-Token";

        private readonly RequestDelegate _next;
        private readonly string? _expectedToken;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<AppTokenGuardMiddleware> _logger;

        public AppTokenGuardMiddleware(
            RequestDelegate next,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILogger<AppTokenGuardMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            ArgumentNullException.ThrowIfNull(configuration);

            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _expectedToken = configuration["Authentication:AppToken"];
        }

        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (!RequiresToken(context))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            if (IsTokenValid(context))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            _logger.LogDebug("Rejecting {Method} {Path} because of missing or invalid {Header}.", context.Request.Method, context.Request.Path, HeaderName);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        }

        private bool RequiresToken(HttpContext context)
        {
            if (string.IsNullOrEmpty(_expectedToken))
                return false;

            if (!context.Request.Path.StartsWithSegments("/api", out var remaining))
                return false;

            if (IsHealthPath(remaining))
                return false;

            if (IsDiagPathBypassed(remaining))
                return false;

            return true;
        }

        private static bool IsHealthPath(PathString remaining)
        {
            if (!remaining.HasValue)
                return false;

            return remaining.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
                   remaining.Equals("/health/", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsDiagPathBypassed(PathString remaining)
        {
            if (!_environment.IsDevelopment())
                return false;

            return remaining.StartsWithSegments("/_diag", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsTokenValid(HttpContext context)
        {
            if (!context.Request.Headers.TryGetValue(HeaderName, out var headerValues))
                return false;

            foreach (var value in headerValues)
            {
                if (string.Equals(value?.Trim(), _expectedToken, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
