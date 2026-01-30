using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace DejtingYarp.Middleware;

/// <summary>
/// Middleware to add standard rate limit headers to responses
/// </summary>
public class RateLimitHeadersMiddleware
{
    private readonly RequestDelegate _next;
    
    public RateLimitHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);
        
        // Add rate limit headers if available
        // Note: These will be populated by ASP.NET Core's rate limiting middleware
        if (context.Response.StatusCode == 429)
        {
            // Ensure headers are present on 429 responses
            if (!context.Response.Headers.ContainsKey("X-RateLimit-Limit"))
            {
                context.Response.Headers["X-RateLimit-Limit"] = "N/A";
            }
            if (!context.Response.Headers.ContainsKey("X-RateLimit-Remaining"))
            {
                context.Response.Headers["X-RateLimit-Remaining"] = "0";
            }
            if (!context.Response.Headers.ContainsKey("X-RateLimit-Reset"))
            {
                context.Response.Headers["X-RateLimit-Reset"] = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds().ToString();
            }
        }
    }
}

/// <summary>
/// Extension methods for rate limit headers middleware
/// </summary>
public static class RateLimitHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseRateLimitHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitHeadersMiddleware>();
    }
}
