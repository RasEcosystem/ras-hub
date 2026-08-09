using System.Collections.ObjectModel;

namespace RasHub.Infrastructure.RasGates.Serialization;

public sealed class RacKeyValueRecord
{
    internal RacKeyValueRecord(IDictionary<string, string> values)
    {
        Values = new ReadOnlyDictionary<string, string>(values);
    }

    public IReadOnlyDictionary<string, string> Values { get; }
}