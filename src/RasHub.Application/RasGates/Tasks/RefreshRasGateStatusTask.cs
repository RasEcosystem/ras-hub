using RasHub.Synchronization.Abstractions;

namespace RasHub.Application.RasGates.Tasks;

public sealed record RefreshRasGateStatusTask(Guid RasGateId)
    : IBackgroundTask;