namespace DejtingYarp.Middleware;

/// <summary>
/// Adds security headers to all responses
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add security headers using OnStarting callback
        // This ensures headers are added just before response starts, but after routing/auth
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // X-Content-Type-Options: Prevents MIME type sniffing
            headers["X-Content-Type-Options"] = "nosniff";

            // X-Frame-Options: Prevents clickjacking
            headers["X-Frame-Options"] = "DENY";

            // X-XSS-Protection: Enables XSS filter in older browsers
            headers["X-XSS-Protection"] = "1; mode=block";

            // Referrer-Policy: Controls referer header
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Content-Security-Policy: Primary defense against XSS
            // Note: Adjust for actual frontend domains in production
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " + // Allow inline scripts for Swagger
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' data:; " +
                "connect-src 'self'; " +
                "frame-ancestors 'none';";

            // Permissions-Policy: Controls browser features
            headers["Permissions-Policy"] =
                "geolocation=(), " +
                "microphone=(), " +
                "camera=(), " +
                "payment=(), " +
                "usb=(), " +
                "magnetometer=(), " +
                "gyroscope=(), " +
                "accelerometer=()";

            // Strict-Transport-Security: Enforce HTTPS (only add if using HTTPS)
            if (context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
            }

            return Task.CompletedTask;
        });

        // Execute next middleware
        await _next(context);
    }
}

/// <summary>
/// Extension methods for SecurityHeadersMiddleware
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
