namespace DejtingYarp.DTOs;

/// <summary>
/// Gateway diagnostics response — route/cluster summary without sensitive details.
/// </summary>
public class GatewayDiagnosticsDto
{
    public string Service { get; set; } = "YarpGateway";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int TotalRoutes { get; set; }
    public int TotalClusters { get; set; }
    public List<RouteInfoDto> Routes { get; set; } = new();
    public List<ClusterInfoDto> Clusters { get; set; } = new();
}

public class RouteInfoDto
{
    public string RouteId { get; set; } = string.Empty;
    public string ClusterId { get; set; } = string.Empty;
    public string MatchPath { get; set; } = string.Empty;
    public List<string> Methods { get; set; } = new();
}

public class ClusterInfoDto
{
    public string ClusterId { get; set; } = string.Empty;
    public int DestinationCount { get; set; }
    public List<DestinationInfoDto> Destinations { get; set; } = new();
    public string LoadBalancingPolicy { get; set; } = string.Empty;
}

public class DestinationInfoDto
{
    public string DestinationId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}
