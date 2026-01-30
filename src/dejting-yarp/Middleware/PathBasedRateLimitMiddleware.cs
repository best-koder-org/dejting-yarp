using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace DejtingYarp.Middleware;

/// <summary>
/// Middleware that applies rate limiting policies based on request path
/// Integrates with ASP.NET Core rate limiting by dynamically setting the policy
/// </summary>
public class PathBasedRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PathBasedRateLimitMiddleware> _logger;
    
    public PathBasedRateLimitMiddleware(
        RequestDelegate next,
        ILogger<PathBasedRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var policyName = DeterminePolicyFromPath(path);
        
        if (!string.IsNullOrEmpty(policyName))
        {
            // Set rate limit policy on the endpoint
            var endpoint = context.GetEndpoint();
            if (endpoint != null)
            {
                var metadata = new List<object>(endpoint.Metadata);
                metadata.Add(new EnableRateLimitingAttribute(policyName));
                
                context.SetEndpoint(new Endpoint(
                    endpoint.RequestDelegate,
                    new EndpointMetadataCollection(metadata),
                    endpoint.DisplayName
                ));
            }
            else
            {
                // No endpoint yet (before routing), create one
                context.SetEndpoint(new Endpoint(
                    _next,
                    new EndpointMetadataCollection(new EnableRateLimitingAttribute(policyName)),
                    $"RateLimit-{policyName}"
                ));
            }
            
            _logger.LogDebug("Applied rate limit policy {Policy} to path {Path}", policyName, path);
        }
        
        await _next(context);
    }
    
    private string? DeterminePolicyFromPath(string path)
    {
        // Messages
        if (path.StartsWith("/api/messages", StringComparison.OrdinalIgnoreCase))
        {
            return "MessagesPerMinute";
        }
        
        // Photos
        if (path.StartsWith("/api/photos", StringComparison.OrdinalIgnoreCase))
        {
            return "PhotoUploadsPerDay";
        }
        
        // User profiles
        if (path.StartsWith("/api/userprofiles", StringComparison.OrdinalIgnoreCase))
        {
            return "ProfileViewsPerMinute";
        }
        
        // Matchmaking
        if (path.StartsWith("/api/matchmaking", StringComparison.OrdinalIgnoreCase))
        {
            return "MatchActionsPerMinute";
        }
        
        // Swipes
        if (path.StartsWith("/api/swipes", StringComparison.OrdinalIgnoreCase))
        {
            return "SwipesPerMinute";
        }
        
        // Safety
        if (path.StartsWith("/api/safety", StringComparison.OrdinalIgnoreCase))
        {
            return "SafetyReportsDaily";
        }
        
        // No rate limiting for other paths (health, auth, etc.)
        return null;
    }
}

public static class PathBasedRateLimitMiddlewareExtensions
{
    public static IApplicationBuilder UsePathBasedRateLimit(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PathBasedRateLimitMiddleware>();
    }
}
