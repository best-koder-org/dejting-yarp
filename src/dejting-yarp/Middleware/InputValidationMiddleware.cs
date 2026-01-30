using System.Text.RegularExpressions;

namespace DejtingYarp.Middleware;

/// <summary>
/// Validates and sanitizes incoming requests to prevent common injection attacks
/// </summary>
public class InputValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<InputValidationMiddleware> _logger;

    // Patterns to detect malicious input
    private static readonly Regex SqlInjectionPattern = new(@"(\b(ALTER|CREATE|DELETE|DROP|EXEC(UTE)?|INSERT( +INTO)?|MERGE|SELECT|UPDATE|UNION( +ALL)?)\b)|('(''|[^'])*')|(--)|(;)|(\bcmd\.exe\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex XssPattern = new(@"<script|javascript:|onerror=|onload=|<iframe|eval\(|expression\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PathTraversalPattern = new(@"\.\./|\.\.\\|%2e%2e|%252e", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> DangerousHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "X-Original-URL",
        "X-Rewrite-URL",
        "X-Arbitrary-Header"
    };

    public InputValidationMiddleware(RequestDelegate next, ILogger<InputValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip validation for health checks
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 1. Validate request headers
        if (HasDangerousHeaders(context.Request.Headers))
        {
            _logger.LogWarning("Dangerous headers detected from {RemoteIp}: {Headers}",
                context.Connection.RemoteIpAddress,
                string.Join(", ", context.Request.Headers.Keys));
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Invalid request headers");
            return;
        }

        // 2. Validate query string parameters
        if (context.Request.Query.Count > 0)
        {
            foreach (var param in context.Request.Query)
            {
                if (IsMaliciousInput(param.Value.ToString()))
                {
                    _logger.LogWarning("Malicious query parameter detected from {RemoteIp}: {Key}={Value}",
                        context.Connection.RemoteIpAddress,
                        param.Key,
                        param.Value);
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Invalid query parameters");
                    return;
                }
            }
        }

        // 3. Validate path for traversal attempts
        if (PathTraversalPattern.IsMatch(context.Request.Path.Value ?? string.Empty))
        {
            _logger.LogWarning("Path traversal attempt detected from {RemoteIp}: {Path}",
                context.Connection.RemoteIpAddress,
                context.Request.Path);
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Invalid request path");
            return;
        }

        // 4. Enforce maximum request size (prevent DoS via large payloads)
        const long maxContentLength = 50 * 1024 * 1024; // 50 MB (photos can be large)
        if (context.Request.ContentLength > maxContentLength)
        {
            _logger.LogWarning("Request body too large from {RemoteIp}: {Size} bytes",
                context.Connection.RemoteIpAddress,
                context.Request.ContentLength);
            context.Response.StatusCode = 413; // Payload Too Large
            await context.Response.WriteAsync("Request too large");
            return;
        }

        await _next(context);
    }

    private bool HasDangerousHeaders(IHeaderDictionary headers)
    {
        return headers.Keys.Any(key => DangerousHeaders.Contains(key));
    }

    private bool IsMaliciousInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        // Check for SQL injection attempts
        if (SqlInjectionPattern.IsMatch(input))
            return true;

        // Check for XSS attempts
        if (XssPattern.IsMatch(input))
            return true;

        // Check for path traversal
        if (PathTraversalPattern.IsMatch(input))
            return true;

        // Check for null bytes (path traversal/injection)
        if (input.Contains('\0'))
            return true;

        return false;
    }
}

/// <summary>
/// Extension methods for InputValidationMiddleware
/// </summary>
public static class InputValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseInputValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<InputValidationMiddleware>();
    }
}
