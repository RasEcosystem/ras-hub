using RasHub.Application.RasGates.Models;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.RasGates.Rac.Infobases.Deserialization;

public sealed class RacInfobaseOutputV1Deserializer(
    RacKeyValueOutputDeserializer keyValueDeserializer)
    : IRacInfobaseOutputDeserializer
{
    public int SchemaVersion => 1;

    public Version MinimumVersion { get; } = new(8, 3, 27, 2214);

    public IReadOnlyList<RasInfobaseSnapshot> Deserialize(string standardOutput)
    {
        var records = keyValueDeserializer.Deserialize(standardOutput);
        var infobases = records
            .Select(DeserializeRecord)
            .ToArray();
        var externalIds = new HashSet<Guid>();

        foreach (var infobase in infobases)
            if (!externalIds.Add(infobase.ExternalId))
                throw new RacOutputDeserializationException(
                    $"RAC output contains duplicate infobase " +
                    $"'{infobase.ExternalId}'.");

        return infobases;
    }

    private static RasInfobaseSnapshot DeserializeRecord(
        RacKeyValueRecord record)
    {
        return new RasInfobaseSnapshot
        {
            ExternalId = ParseGuid(record, "infobase"),
            Name = Unquote(GetRequiredValue(record, "name")),
            Description = Unquote(GetRequiredValue(record, "descr"))
        };
    }

    private static Guid ParseGuid(RacKeyValueRecord record, string key)
    {
        if (Guid.TryParse(GetRequiredValue(record, key), out var value) &&
            value != Guid.Empty)
            return value;

        throw InvalidValue(key);
    }

    private static string GetRequiredValue(
        RacKeyValueRecord record,
        string key)
    {
        if (record.Values.TryGetValue(key, out var value))
            return value;

        throw new RacOutputDeserializationException(
            $"RAC output record does not contain required key '{key}'.");
    }

    private static string Unquote(string value)
    {
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1]
            : value;
    }

    private static RacOutputDeserializationException InvalidValue(string key)
    {
        return new RacOutputDeserializationException(
            $"RAC output contains an invalid value for key '{key}'.");
    }
}
