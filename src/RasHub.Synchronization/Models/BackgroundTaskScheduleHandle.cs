namespace RasHub.Synchronization.Models;

/// <summary>
///     Owns a periodic schedule registration; disposing the handle removes that schedule.
/// </summary>
public sealed class BackgroundTaskScheduleHandle : IDisposable
{
    private readonly Func<string, bool> _remove;
    private int _disposed;

    internal BackgroundTaskScheduleHandle(
        string id,
        Func<string, bool> remove)
    {
        Id = id;
        _remove = remove;
    }

    public string Id { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _remove(Id);
    }
}