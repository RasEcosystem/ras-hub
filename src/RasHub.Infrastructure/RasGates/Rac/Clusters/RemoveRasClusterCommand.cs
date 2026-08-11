namespace RasHub.Infrastructure.RasGates.Rac.Clusters;

public sealed record RemoveRasClusterCommand(
    Guid ClusterId,
    string? ClusterUser,
    string? ClusterPassword);