using System.Collections.Concurrent;

namespace RasHub.Synchronization.Internal.Processing;

/// <summary>
///     Provides a process-wide exclusive lease for each non-null concurrency key.
/// </summary>
internal sealed class BackgroundTaskConcurrencyGate
{
    private readonly ConcurrentDictionary<string, byte> _active =
        new(StringComparer.Ordinal);

    /// <summary>Number of concurrency keys currently held by running attempts.</summary>
    public int ActiveKeyCount => _active.Count;

    public bool TryAcquire(
        string? key,
        out IDisposable? lease)
    {
        if (key is null)
        {
            lease = null;
            return true;
        }

        if (!_active.TryAdd(key, 0))
        {
            lease = null;
            return false;
        }

        lease = new Lease(_active, key);
        return true;
    }

    /// <summary>Releases the concurrency key exactly once when an attempt leaves its critical section.</summary>
    private sealed class Lease(
        ConcurrentDictionary<string, byte> active,
        string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                active.TryRemove(key, out _);
        }
    }
}