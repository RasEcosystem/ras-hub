using RasHub.Application.RasGates.Models;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters.Commands;

public sealed record UpdateRasClusterCommand(
    Guid ClusterId,
    RasClusterUpdateOptions Options);