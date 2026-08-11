using RasHub.Application.RasGates.Models;
using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks;

public sealed record UpdateClusterTask(
    Guid RasGateId,
    Guid ClusterId,
    RasClusterUpdateOptions Options)
    : IBackgroundTask;