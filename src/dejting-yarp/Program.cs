using System;
using System.Threading.RateLimiting;
using DatingApp.Shared.Middleware;
using DejtingYarp.Extensions;
using DejtingYarp.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithCorrelationId()
    .Enrich.WithProperty("ServiceName", "YarpGateway")
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .MinimumLevel.Override("Yarp", LogEventLevel.Information)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/yarp-gateway-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ServiceName}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"
    ));

// Add configuration sources
if (builder.Environment.EnvironmentName == "Local")
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: false, reloadOnChange: true);
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddCorrelationIds();

// Configure rate limiting
builder.Services.AddRateLimiter(options =>
{
    // Messages policy: 10 messages per minute
    options.AddSlidingWindowLimiter("MessagesPerMinute", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 10;
        opt.QueueLimit = 0;
        opt.SegmentsPerWindow = 2;
    });
    
    // Photo uploads: 20 per day
    options.AddSlidingWindowLimiter("PhotoUploadsPerDay", opt =>
    {
        opt.Window = TimeSpan.FromDays(1);
        opt.PermitLimit = 20;
        opt.QueueLimit = 0;
        opt.SegmentsPerWindow = 6;
    });
    
    // Profile views: 60 per minute
    options.AddSlidingWindowLimiter("ProfileViewsPerMinute", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 60;
        opt.QueueLimit = 0;
        opt.SegmentsPerWindow = 4;
    });
    
    // Profile updates: 10 per hour
    options.AddSlidingWindowLimiter("ProfileUpdatesPerHour", opt =>
    {
        opt.Window = TimeSpan.FromHours(1);
        opt.PermitLimit = 10;
        opt.QueueLimit = 0;
        opt.SegmentsPerWindow = 2;
    });
    
    // Match actions: 20 per minute
    options.AddSlidingWindowLimiter("MatchActionsPerMinute", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 20;
        opt.QueueLimit = 0;
        opt.SegmentsPerWindow = 2;
    });
    
    // Swipes: 60 per minute (existing policy, redefined here)
    options.AddSlidingWindowLimiter("SwipesPerMinute", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 60;
        opt.QueueLimit = 0;
        opt.SegmentsPerWindow = 4;
    });
    
    // Safety reports: 10 per day (existing policy, redefined here)
    options.AddSlidingWindowLimiter("SafetyReportsDaily", opt =>
    {
        opt.Window = TimeSpan.FromDays(1);
        opt.PermitLimit = 10;
        opt.QueueLimit = 0;
    });
    
    // Partition by user ID from JWT token
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Get user ID from JWT token (sub claim) or default to "anonymous"
        var userId = context.User?.FindFirst("sub")?.Value 
                     ?? context.User?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "anonymous";
        
        // For YARP routes with rate limit metadata, return appropriate partition
        // This will be handled by YARP's built-in rate limiting integration
        return RateLimitPartition.GetNoLimiter<string>(userId);
    });
    
    // Global error handling for rate limit exceeded
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers["Content-Type"] = "application/json";
        
        var retryAfter = 60; // Default retry after
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterTimeSpan))
        {
            retryAfter = (int)retryAfterTimeSpan.TotalSeconds;
            context.HttpContext.Response.Headers["Retry-After"] = retryAfter.ToString();
        }
        
        // Add standard rate limit headers
        context.HttpContext.Response.Headers["X-RateLimit-Limit"] = "N/A";
        context.HttpContext.Response.Headers["X-RateLimit-Remaining"] = "0";
        context.HttpContext.Response.Headers["X-RateLimit-Reset"] = DateTimeOffset.UtcNow.AddSeconds(retryAfter).ToUnixTimeSeconds().ToString();
        
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded",
            message = "Too many requests. Please try again later.",
            retryAfterSeconds = retryAfter
        }, cancellationToken);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Dejting YARP API",
        Version = "v1",
        Description = "API documentation for the Dejting YARP Gateway."
    });
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Dejting YARP API v1");
        c.RoutePrefix = string.Empty;
    });
}

// Enforce HTTPS in production (before CORS)
app.UseHttpsEnforcement();

app.UseCors("AllowAll");

// Enable WebSockets support for SignalR
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
};
app.UseWebSockets(webSocketOptions);

app.UseRouting();

// Input validation (protects against SQL injection, XSS, path traversal)
app.UseInputValidation();

// Add security headers (HIGH priority - protects against XSS, clickjacking, MIME sniffing)
app.UseSecurityHeaders();

app.UseCorrelationIds();
app.UsePathBasedRateLimit(); // Apply rate limiting based on request path
app.UseRateLimiter(); // ASP.NET Core rate limiting middleware
app.UseRateLimitHeaders(); // Add X-RateLimit-* headers
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.Use(async (context, next) =>
    {
        // Allow auth endpoints without authentication
        if (context.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        // Allow websocket connections (SignalR) to pass through - auth handled by hub via query string token
        if (context.WebSockets.IsWebSocketRequest || 
            context.Request.Path.StartsWithSegments("/messagingHub", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var authService = context.RequestServices.GetRequiredService<IAuthenticationService>();
        var authenticateResult = await authService.AuthenticateAsync(context, JwtBearerDefaults.AuthenticationScheme);

        if (!authenticateResult.Succeeded || authenticateResult.Principal is null)
        {
            await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
            return;
        }

        context.User = authenticateResult.Principal;

        await next();
    });
});
app.Urls.Add("http://*:8080");

app.Run();

// Make Program accessible to test project (for WebApplicationFactory)
public partial class Program { }
