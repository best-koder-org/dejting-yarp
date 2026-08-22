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

        return await FanOutAsync("reset-interactions", targets, bearer, ct);
    }

    /// <summary>
    /// Composite targeted purge — fans out to the bot-only delete endpoints on each service
    /// (DELETE /api/admin/bot-*). Only bot-generated rows are removed; real-user data is
    /// never touched. Forwards the caller's bearer token unchanged.
    /// </summary>
    [HttpPost("reset-bot-interactions")]
    public async Task<IActionResult> ResetBotInteractions([FromQuery] int olderThanHours = 0, CancellationToken ct = default)
    {
        var bearer = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(bearer))
        {
            return Unauthorized(new { error = "Missing Authorization header" });
        }

        var ttl = olderThanHours > 0 ? $"?olderThanHours={olderThanHours}" : string.Empty;
        var targets = new[]
        {
            new { Name = "matchmaking", Url = $"{ServiceUrl("Matchmaking", "http://localhost:8083")}/api/admin/bot-match-data{ttl}" },
            new { Name = "messaging",   Url = $"{ServiceUrl("Messaging",   "http://localhost:8086")}/api/admin/bot-messages{ttl}" },
            new { Name = "swipe",       Url = $"{ServiceUrl("Swipe",       "http://localhost:8087")}/api/admin/bot-swipe-data{ttl}" }
        };

        return await FanOutAsync("reset-bot-interactions", targets, bearer, ct);
    }

    private async Task<IActionResult> FanOutAsync(string op, IEnumerable<object> targets, string bearer, CancellationToken ct)
    {
        var results = new List<object>();
        var anyFailed = false;

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        foreach (var t in targets)
        {
            var name = t.GetType().GetProperty("Name")!.GetValue(t)!.ToString()!;
            var url = t.GetType().GetProperty("Url")!.GetValue(t)!.ToString()!;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Delete, url);
                req.Headers.TryAddWithoutValidation("Authorization", bearer);
                using var resp = await client.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) { anyFailed = true; }
                results.Add(new
                {
                    service = name,
                    status = (int)resp.StatusCode,
                    ok = resp.IsSuccessStatusCode,
                    body
                });
            }
            catch (Exception ex)
            {
                anyFailed = true;
                _logger.LogError(ex, "AdminReset: {Service} failed", name);
                results.Add(new { service = name, status = 0, ok = false, body = ex.Message });
            }
        }

        var severity = op == "reset-interactions" ? "High" : "Medium";
        _logger.LogWarning("[FINDING] {Severity} AdminReset: composite {Op} by {User}; anyFailed={Failed}",
            severity, op, User.Identity?.Name ?? "unknown", anyFailed);

        return StatusCode(anyFailed ? 207 : 200, new { results });
    }

    private string ServiceUrl(string name, string fallback)
    {
        return _configuration[$"AdminReset:{name}"] ?? fallback;
    }
}
