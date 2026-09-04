using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Clusters;

public sealed record UpdateClusterTask(
    Guid RasEndpointId,
    Guid ClusterId,
    RasClusterUpdateOptions Options)
    : IBackgroundTask;
