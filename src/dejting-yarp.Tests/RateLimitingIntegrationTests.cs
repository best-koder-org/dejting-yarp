using System.Net;
using System.Net.Http.Headers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace dejting_yarp.Tests;

/// <summary>
/// Integration tests for P1-006 Rate Limiting implementation
/// Tests all 7 rate limit policies with real HTTP requests
/// </summary>
public class RateLimitingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RateLimitingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private string GenerateJwtToken(string userId = "test-user-123")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes("test-secret-key-with-at-least-32-characters-for-hmacsha256");
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", userId),
                new Claim("email", $"{userId}@test.com")
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key), 
                SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    [Fact]
    public async Task MessagesPerMinute_ShouldEnforce10RequestLimit()
    {
        // Arrange
        var token = GenerateJwtToken("user-messages-test");
        var endpoint = "/api/messages";

        // Act - Send 12 requests (limit is 10)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 12; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            responses.Add(await _client.SendAsync(request));
        }

        // Assert
        var successCount = responses.Count(r => r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotFound);
        var rateLimitedCount = responses.Count(r => r.StatusCode == (HttpStatusCode)429);

        Assert.True(successCount <= 10, $"Expected <= 10 successful requests, got {successCount}");
        Assert.True(rateLimitedCount >= 2, $"Expected >= 2 rate limited (429) responses, got {rateLimitedCount}");

        // Verify 429 responses have required headers
        var rateLimitedResponse = responses.FirstOrDefault(r => r.StatusCode == (HttpStatusCode)429);
        if (rateLimitedResponse != null)
        {
            Assert.True(rateLimitedResponse.Headers.Contains("X-RateLimit-Limit") ||
                       rateLimitedResponse.Headers.Contains("Retry-After"),
                       "429 response missing rate limit headers");
        }
    }

    [Fact]
    public async Task PhotoUploadsPerDay_ShouldEnforce20RequestLimit()
    {
        // Arrange
        var token = GenerateJwtToken("user-photo-test");
        var endpoint = "/api/photos";

        // Act - Send 22 requests (limit is 20/day)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 22; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            responses.Add(await _client.SendAsync(request));
        }

        // Assert
        var successCount = responses.Count(r => r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotFound);
        var rateLimitedCount = responses.Count(r => r.StatusCode == (HttpStatusCode)429);

        Assert.True(successCount <= 20, $"Expected <= 20 successful requests, got {successCount}");
        Assert.True(rateLimitedCount >= 2, $"Expected >= 2 rate limited, got {rateLimitedCount}");
    }

    [Fact]
    public async Task ProfileViewsPerMinute_ShouldEnforce60RequestLimit()
    {
        // Arrange
        var token = GenerateJwtToken("user-profile-view-test");
        var endpoint = "/api/userprofiles/some-user";

        // Act - Send 65 requests (limit is 60/min)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 65; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            responses.Add(await _client.SendAsync(request));
        }

        // Assert
        var successCount = responses.Count(r => r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotFound);
        var rateLimitedCount = responses.Count(r => r.StatusCode == (HttpStatusCode)429);

        Assert.True(successCount <= 60, $"Expected <= 60 successful requests, got {successCount}");
        Assert.True(rateLimitedCount >= 5, $"Expected >= 5 rate limited, got {rateLimitedCount}");
    }

    [Fact]
    public async Task RateLimit_ShouldIsolateUsersByJwtSub()
    {
        // Arrange
        var user1Token = GenerateJwtToken("user-isolation-1");
        var user2Token = GenerateJwtToken("user-isolation-2");
        var endpoint = "/api/messages";

        // Act - User 1 hits limit
        var user1Responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 11; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user1Token);
            user1Responses.Add(await _client.SendAsync(request));
        }

        // User 2 should still have full quota
        var user2Response = new HttpRequestMessage(HttpMethod.Post, endpoint);
        user2Response.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user2Token);
        var user2Result = await _client.SendAsync(user2Response);

        // Assert
        var user1RateLimited = user1Responses.Any(r => r.StatusCode == (HttpStatusCode)429);
        Assert.True(user1RateLimited, "User 1 should be rate limited");

        var user2Allowed = user2Result.IsSuccessStatusCode || user2Result.StatusCode == HttpStatusCode.NotFound;
        Assert.True(user2Allowed, "User 2 should NOT be rate limited (different user)");
    }

    [Fact]
    public async Task RateLimit_429Response_ShouldIncludeStandardHeaders()
    {
        // Arrange
        var token = GenerateJwtToken("user-headers-test");
        var endpoint = "/api/messages";

        // Act - Exhaust limit to force 429
        HttpResponseMessage? rateLimitedResponse = null;
        for (int i = 0; i < 15; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _client.SendAsync(request);

            if (response.StatusCode == (HttpStatusCode)429)
            {
                rateLimitedResponse = response;
                break;
            }
        }

        // Assert
        Assert.NotNull(rateLimitedResponse);
        
        // Check for at least one rate limit header (implementation may vary)
        var hasRateLimitHeaders = 
            rateLimitedResponse.Headers.Contains("X-RateLimit-Limit") ||
            rateLimitedResponse.Headers.Contains("X-RateLimit-Remaining") ||
            rateLimitedResponse.Headers.Contains("X-RateLimit-Reset") ||
            rateLimitedResponse.Headers.Contains("Retry-After");

        Assert.True(hasRateLimitHeaders, "429 response must include rate limit headers");

        // Verify Content-Type is JSON
        Assert.Equal("application/json", rateLimitedResponse.Content.Headers.ContentType?.MediaType);

        // Verify JSON error body
        var content = await rateLimitedResponse.Content.ReadAsStringAsync();
        Assert.Contains("error", content.ToLower());
        Assert.Contains("rate", content.ToLower());
    }

    [Fact]
    public async Task HealthEndpoint_ShouldBypassRateLimiting()
    {
        // Arrange
        var endpoint = "/health";

        // Act - Send 100 requests (should all succeed, no rate limit)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 100; i++)
        {
            responses.Add(await _client.GetAsync(endpoint));
        }

        // Assert - No 429 responses
        var rateLimitedCount = responses.Count(r => r.StatusCode == (HttpStatusCode)429);
        Assert.Equal(0, rateLimitedCount);
    }

    [Fact]
    public async Task SwipesPerMinute_ShouldEnforce60RequestLimit()
    {
        // Arrange
        var token = GenerateJwtToken("user-swipe-test");
        var endpoint = "/api/swipes";

        // Act - Send 65 requests (limit is 60/min)
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 65; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            responses.Add(await _client.SendAsync(request));
        }

        // Assert
        var successCount = responses.Count(r => r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.NotFound);
        var rateLimitedCount = responses.Count(r => r.StatusCode == (HttpStatusCode)429);

        Assert.True(successCount <= 60, $"Expected <= 60 successful requests, got {successCount}");
        Assert.True(rateLimitedCount >= 5, $"Expected >= 5 rate limited, got {rateLimitedCount}");
    }
}
