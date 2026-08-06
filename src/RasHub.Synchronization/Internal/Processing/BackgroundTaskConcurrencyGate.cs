using System.Collections.Concurrent;

namespace RasHub.Synchronization.Internal;

internal sealed class BackgroundTaskConcurrencyGate
{
    private readonly ConcurrentDictionary<string, byte> _active =
        new(StringComparer.Ordinal);

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