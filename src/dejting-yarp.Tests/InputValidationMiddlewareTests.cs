using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using DejtingYarp.Middleware;

namespace dejting_yarp.Tests;

public class InputValidationMiddlewareTests
{
    private readonly InputValidationMiddleware _middleware;
    private readonly DefaultHttpContext _context;
    private bool _nextCalled;

    public InputValidationMiddlewareTests()
    {
        RequestDelegate next = _ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        };
        _middleware = new InputValidationMiddleware(next, Mock.Of<ILogger<InputValidationMiddleware>>());
        _context = new DefaultHttpContext();
        _context.Request.Method = "GET";
        _context.Request.Path = "/api/users";
        _nextCalled = false;
    }

    // ===== Bypass Paths =====

    [Theory]
    [InlineData("/health")]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    public async Task HealthAndSwagger_Bypass_CallsNext(string path)
    {
        _context.Request.Path = path;
        await _middleware.InvokeAsync(_context);
        Assert.True(_nextCalled);
    }

    // ===== Legitimate Requests =====

    [Fact]
    public async Task CleanRequest_Passes()
    {
        _context.Request.Path = "/api/users/1";
        _context.Request.QueryString = new QueryString("?name=Alice&age=25");

        await _middleware.InvokeAsync(_context);

        Assert.True(_nextCalled);
        Assert.Equal(200, _context.Response.StatusCode);
    }

    // ===== SQL Injection Detection =====

    [Theory]
    [InlineData("?search='; DROP TABLE users;--")]
    [InlineData("?q=1 UNION SELECT * FROM passwords")]
    [InlineData("?q=admin' OR '1'='1")]
    [InlineData("?q=1; DELETE FROM users")]
    [InlineData("?input=test' AND 1=1--")]
    public async Task SqlInjection_InQuery_Returns400(string query)
    {
        _context.Request.QueryString = new QueryString(query);

        await _middleware.InvokeAsync(_context);

        Assert.Equal(400, _context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    // ===== XSS Detection =====

    [Theory]
    [InlineData("?name=<script>alert('xss')</script>")]
    [InlineData("?url=javascript:alert(1)")]
    [InlineData("?img=<iframe src=evil.com>")]
    [InlineData("?handler=onerror=alert(1)")]
    [InlineData("?data=eval(document.cookie)")]
    public async Task XssAttack_InQuery_Returns400(string query)
    {
        _context.Request.QueryString = new QueryString(query);

        await _middleware.InvokeAsync(_context);

        Assert.Equal(400, _context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    // ===== Path Traversal Detection =====

    [Theory]
    [InlineData("/api/../../../etc/passwd")]
    [InlineData("/api/..\\..\\windows\\system32")]
    [InlineData("/api/%2e%2e/%2e%2e/etc/shadow")]
    public async Task PathTraversal_Returns400(string path)
    {
        _context.Request.Path = path;

        await _middleware.InvokeAsync(_context);

        Assert.Equal(400, _context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    // ===== Dangerous Headers =====

    [Theory]
    [InlineData("X-Original-URL", "/admin")]
    [InlineData("X-Rewrite-URL", "/internal")]
    [InlineData("X-Arbitrary-Header", "test")]
    public async Task DangerousHeaders_Returns400(string header, string value)
    {
        _context.Request.Headers[header] = value;

        await _middleware.InvokeAsync(_context);

        Assert.Equal(400, _context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    // ===== Null Bytes =====

    [Fact]
    public async Task NullByte_InQuery_Returns400()
    {
        _context.Request.QueryString = new QueryString("?file=test\0.jpg");

        await _middleware.InvokeAsync(_context);

        Assert.Equal(400, _context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    // ===== Oversized Body =====

    [Fact]
    public async Task OversizedBody_Returns413()
    {
        _context.Request.Method = "POST";
        _context.Request.ContentLength = 51 * 1024 * 1024; // 51 MB > 50 MB limit

        await _middleware.InvokeAsync(_context);

        Assert.Equal(413, _context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task NormalSizedBody_Passes()
    {
        _context.Request.Method = "POST";
        _context.Request.ContentLength = 1024; // 1 KB

        await _middleware.InvokeAsync(_context);

        Assert.True(_nextCalled);
    }
}
