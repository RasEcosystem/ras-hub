using RasHub.Synchronization.Abstractions;

namespace RasHub.Synchronization.Tasks;

/// <summary>
///     Synthetic workload that remains active for the requested duration.
///     Intended for interactively exercising synchronization lanes and monitoring.
/// </summary>
public sealed record TestBackgroundTask(TimeSpan Duration) : IBackgroundTask;