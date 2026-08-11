using RasHub.Application.RasGates.Models;

namespace RasHub.Infrastructure.RasGates.Rac.Clusters;

public sealed record UpdateRasClusterCommand(
    Guid ClusterId,
    RasClusterUpdateOptions Options);