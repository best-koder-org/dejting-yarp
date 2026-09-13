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
        // Registered BEFORE _next on purpose.
        //
        // This used to write the headers after `await _next(context)`, which is too late:
        // by then the proxied response has already been streamed to the client, and mutating
        // Response.Headers on a started response corrupts the chunked framing — the
        // terminating `0\r\n\r\n` chunk never goes out. Strict clients (Dart's HttpClient,
        // Python's http.client) then fail the whole request with an incomplete-read error
        // and the app reports "network error" instead of the real 429 message.
        //
        // It only ever showed on 429 because the `if` below meant every other status took
        // the no-op path. OnStarting runs while the response is still writable, which is the
        // supported place to add headers for a proxied response.
        context.Response.OnStarting(static state =>
        {
            var response = (HttpResponse)state;

            if (response.StatusCode == 429)
            {
                if (!response.Headers.ContainsKey("X-RateLimit-Limit"))
                {
                    response.Headers["X-RateLimit-Limit"] = "N/A";
                }
                if (!response.Headers.ContainsKey("X-RateLimit-Remaining"))
                {
                    response.Headers["X-RateLimit-Remaining"] = "0";
                }
                if (!response.Headers.ContainsKey("X-RateLimit-Reset"))
                {
                    response.Headers["X-RateLimit-Reset"] =
                        DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds().ToString();
                }
            }

            return Task.CompletedTask;
        }, context.Response);

        await _next(context);
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
