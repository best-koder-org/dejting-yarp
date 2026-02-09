using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DejtingYarp.DTOs;
using Yarp.ReverseProxy;

namespace DejtingYarp.Controllers;

/// <summary>
/// Gateway diagnostics — exposes YARP route/cluster configuration (read-only).
/// Useful for debugging routing issues and verifying gateway state.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[AllowAnonymous] // Internal endpoint — protected by network policy in production
public class DiagnosticsController : ControllerBase
{
    private readonly IProxyStateLookup _proxyState;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(IProxyStateLookup proxyState, ILogger<DiagnosticsController> logger)
    {
        _proxyState = proxyState;
        _logger = logger;
    }

    /// <summary>
    /// Get full gateway diagnostics — all routes and clusters.
    /// </summary>
    [HttpGet]
    public IActionResult GetDiagnostics()
    {
        var routes = _proxyState.GetRoutes().Select(r => new RouteInfoDto
        {
            RouteId = r.Config.RouteId,
            ClusterId = r.Config.ClusterId ?? "",
            MatchPath = r.Config.Match.Path ?? "",
            Methods = r.Config.Match.Methods?.ToList() ?? new List<string>()
        }).ToList();

        var clusters = _proxyState.GetClusters().Select(c => new ClusterInfoDto
        {
            ClusterId = c.ClusterId,
            DestinationCount = c.DestinationsState?.AllDestinations.Count ?? 0,
            LoadBalancingPolicy = c.Model?.Config?.LoadBalancingPolicy ?? "default",
            Destinations = c.DestinationsState?.AllDestinations.Select(d => new DestinationInfoDto
            {
                DestinationId = d.DestinationId,
                Address = d.Model?.Config?.Address ?? "unknown"
            }).ToList() ?? new List<DestinationInfoDto>()
        }).ToList();

        return Ok(new GatewayDiagnosticsDto
        {
            TotalRoutes = routes.Count,
            TotalClusters = clusters.Count,
            Routes = routes,
            Clusters = clusters
        });
    }

    /// <summary>
    /// Get just route summary — lightweight check.
    /// </summary>
    [HttpGet("routes")]
    public IActionResult GetRoutes()
    {
        var routes = _proxyState.GetRoutes().Select(r => new RouteInfoDto
        {
            RouteId = r.Config.RouteId,
            ClusterId = r.Config.ClusterId ?? "",
            MatchPath = r.Config.Match.Path ?? "",
            Methods = r.Config.Match.Methods?.ToList() ?? new List<string>()
        }).ToList();

        return Ok(routes);
    }

    /// <summary>
    /// Get just cluster summary.
    /// </summary>
    [HttpGet("clusters")]
    public IActionResult GetClusters()
    {
        var clusters = _proxyState.GetClusters().Select(c => new ClusterInfoDto
        {
            ClusterId = c.ClusterId,
            DestinationCount = c.DestinationsState?.AllDestinations.Count ?? 0,
            LoadBalancingPolicy = c.Model?.Config?.LoadBalancingPolicy ?? "default"
        }).ToList();

        return Ok(clusters);
    }
}
