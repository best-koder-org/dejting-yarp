using System;
using DatingApp.Shared.Middleware;
using DejtingYarp.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

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

app.UseCors("AllowAll");

// Enable WebSockets support for SignalR
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
};
app.UseWebSockets(webSocketOptions);

app.UseRouting();

app.UseCorrelationIds();
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

// Configure the application to listen on port 8081
app.Urls.Add("http://*:8080");

app.Run();
