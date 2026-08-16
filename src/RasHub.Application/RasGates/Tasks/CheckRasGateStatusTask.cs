using RasHub.BackgroundTasks.Abstractions;

namespace RasHub.Application.RasGates.Tasks;

public sealed record CheckRasGateStatusTask(Guid RasGateId)
    : IBackgroundTask;