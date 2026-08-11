namespace RasHub.Infrastructure.RasGates.Client;

public sealed class RacVersionCache(TimeProvider timeProvider)
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(5);

    private readonly Dictionary<CacheKey, CacheEntry> _entries = [];
    private readonly object _lock = new();

    public bool TryGet(
        Guid rasGateId,
        long configurationRevision,
        out Version version)
    {
        var key = new CacheKey(rasGateId, configurationRevision);
        var now = timeProvider.GetUtcNow();

        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var entry) &&
                entry.ExpiresAt > now)
            {
                version = entry.Version;
                return true;
            }

            _entries.Remove(key);
        }

        version = null!;
        return false;
    }

    public void Set(
        Guid rasGateId,
        long configurationRevision,
        Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var now = timeProvider.GetUtcNow();
        var key = new CacheKey(rasGateId, configurationRevision);

        lock (_lock)
        {
            foreach (var obsoleteKey in _entries
                         .Where(item => item.Value.ExpiresAt <= now)
                         .Select(item => item.Key)
                         .ToArray())
                _entries.Remove(obsoleteKey);

            _entries[key] = new CacheEntry(version, now + EntryLifetime);
        }
    }

    public void Remove(Guid rasGateId, long configurationRevision)
    {
        lock (_lock)
        {
            _entries.Remove(new CacheKey(rasGateId, configurationRevision));
        }
    }

    private readonly record struct CacheKey(
        Guid RasGateId,
        long ConfigurationRevision);

    private sealed record CacheEntry(
        Version Version,
        DateTimeOffset ExpiresAt);
}