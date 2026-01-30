namespace DejtingYarp.Middleware;

/// <summary>
/// Redirects HTTP requests to HTTPS in production
/// </summary>
public class HttpsEnforcementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;
    private readonly ILogger<HttpsEnforcementMiddleware> _logger;

    public HttpsEnforcementMiddleware(
        RequestDelegate next,
        IHostEnvironment env,
        ILogger<HttpsEnforcementMiddleware> logger)
    {
        _next = next;
        _env = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip HTTPS enforcement in development
        if (_env.IsDevelopment())
        {
            await _next(context);
            return;
        }

        // Check if request is already HTTPS
        if (context.Request.IsHttps)
        {
            await _next(context);
            return;
        }

        // Check for forwarded proto header (load balancer/reverse proxy scenarios)
        if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && 
            proto.ToString().Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Redirect to HTTPS
        var httpsUrl = $"https://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
        _logger.LogInformation("Redirecting HTTP request to HTTPS: {HttpsUrl}", httpsUrl);
        
        context.Response.StatusCode = 301; // Permanent redirect
        context.Response.Headers["Location"] = httpsUrl;
    }
}

/// <summary>
/// Extension methods for HttpsEnforcementMiddleware
/// </summary>
public static class HttpsEnforcementMiddlewareExtensions
{
    public static IApplicationBuilder UseHttpsEnforcement(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<HttpsEnforcementMiddleware>();
    }
}
