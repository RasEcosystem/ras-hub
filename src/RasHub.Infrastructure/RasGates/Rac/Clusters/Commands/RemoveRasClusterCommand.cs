namespace RasHub.Infrastructure.RasGates.Rac.Clusters.Commands;

public sealed record RemoveRasClusterCommand(
    Guid ClusterId,
    string? ClusterUser,
    string? ClusterPassword);
