using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DejtingYarp.Controllers;

/// <summary>
/// Composite admin reset — fans out a single user-initiated reset to all
/// three downstream services (matchmaking, messaging, swipe). Each service
/// has its own DELETE /api/admin/* endpoint guarded by [Authorize] and an
/// environment check (Dev/Staging/Demo only).
///
/// This endpoint forwards the caller's bearer token unchanged so each
/// downstream service can perform its own auth check.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminResetController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminResetController> _logger;

    public AdminResetController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AdminResetController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("reset-interactions")]
    public async Task<IActionResult> ResetInteractions(CancellationToken ct)
    {
        var bearer = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(bearer))
        {
            return Unauthorized(new { error = "Missing Authorization header" });
        }

        var targets = new[]
        {
            new { Name = "matchmaking", Url = $"{ServiceUrl("Matchmaking", "http://localhost:8083")}/api/admin/matches" },
            new { Name = "messaging",   Url = $"{ServiceUrl("Messaging",   "http://localhost:8086")}/api/admin/messages" },
            new { Name = "swipe",       Url = $"{ServiceUrl("Swipe",       "http://localhost:8087")}/api/admin/swipes" }
        };

        var results = new List<object>();
        var anyFailed = false;

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        foreach (var t in targets)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Delete, t.Url);
                req.Headers.TryAddWithoutValidation("Authorization", bearer);
                using var resp = await client.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) { anyFailed = true; }
                results.Add(new
                {
                    service = t.Name,
                    status = (int)resp.StatusCode,
                    ok = resp.IsSuccessStatusCode,
                    body
                });
            }
            catch (Exception ex)
            {
                anyFailed = true;
                _logger.LogError(ex, "AdminReset: {Service} failed", t.Name);
                results.Add(new { service = t.Name, status = 0, ok = false, body = ex.Message });
            }
        }

        _logger.LogWarning("[FINDING] High AdminReset: composite reset by {User}; anyFailed={Failed}",
            User.Identity?.Name ?? "unknown", anyFailed);

        return StatusCode(anyFailed ? 207 : 200, new { results });
    }

    private string ServiceUrl(string name, string fallback)
    {
        return _configuration[$"AdminReset:{name}"] ?? fallback;
    }
}
