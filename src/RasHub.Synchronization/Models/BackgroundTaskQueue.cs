namespace RasHub.Synchronization.Models;

/// <summary>
///     Isolated queue lanes with independent capacities and worker quotas.
/// </summary>
public enum BackgroundTaskQueue
{
    /// <summary>User-facing work whose result may be awaited by a request.</summary>
    Interactive = 0,

    /// <summary>Regular background synchronization work.</summary>
    Synchronization = 1,

    /// <summary>Low-priority cleanup and system maintenance work.</summary>
    Maintenance = 2
}