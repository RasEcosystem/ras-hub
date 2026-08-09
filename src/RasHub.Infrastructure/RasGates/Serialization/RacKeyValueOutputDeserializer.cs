using RasHub.Application.RasGates.Serialization;

namespace RasHub.Infrastructure.RasGates.Serialization;

public sealed class RacKeyValueOutputDeserializer
    : IRacOutputDeserializer<IReadOnlyList<RacKeyValueRecord>>
{
    public IReadOnlyList<RacKeyValueRecord> Deserialize(string standardOutput)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);

        var records = new List<RacKeyValueRecord>();
        var values = CreateValueDictionary();
        using var reader = new StringReader(standardOutput);
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                AddRecord(records, values);
                values = CreateValueDictionary();
                continue;
            }

            var separatorIndex = line.IndexOf(':');

            if (separatorIndex < 0)
                throw new RacOutputDeserializationException(
                    $"RAC output line {lineNumber} does not contain a key-value separator.");

            var key = line[..separatorIndex].Trim();

            if (key.Length == 0)
                throw new RacOutputDeserializationException(
                    $"RAC output line {lineNumber} has an empty key.");

            var value = line[(separatorIndex + 1)..].Trim();

            if (!values.TryAdd(key, value))
                throw new RacOutputDeserializationException(
                    $"RAC output record contains duplicate key '{key}' at line {lineNumber}.");
        }

        AddRecord(records, values);

        return records;
    }

    private static Dictionary<string, string> CreateValueDictionary()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddRecord(
        ICollection<RacKeyValueRecord> records,
        IDictionary<string, string> values)
    {
        if (values.Count > 0)
            records.Add(new RacKeyValueRecord(values));
    }
}