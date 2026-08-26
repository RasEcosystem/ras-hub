using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks.Status;

public sealed record CheckRasGateStatusTask(Guid RasGateId)
    : IBackgroundTask;
