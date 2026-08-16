using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Parsing;

public sealed class RacKeyValueOutputDeserializerTests
{
    private readonly RacKeyValueOutputDeserializer _deserializer = new();

    [Fact]
    public void Deserialize_parses_real_cluster_list_output_without_losing_values()
    {
        const string output =
            "cluster                                   : 820d1955-349e-4173-9092-a3f206d328f7\r\n" +
            "host                                      : WIN-P4BDRRBVMU8\r\n" +
            "port                                      : 1541\r\n" +
            "name                                      : \"Локальный кластер\"\r\n" +
            "expiration-timeout                        : 60\r\n" +
            "lifetime-limit                            : 0\r\n" +
            "max-memory-size                           : 0\r\n" +
            "max-memory-time-limit                     : 0\r\n" +
            "security-level                            : 0\r\n" +
            "session-fault-tolerance-level             : 0\r\n" +
            "load-balancing-mode                       : performance\r\n" +
            "errors-count-threshold                    : 0\r\n" +
            "kill-problem-processes                    : 1\r\n" +
            "kill-by-memory-with-dump                  : 0\r\n" +
            "allow-access-right-audit-events-recording : 0\r\n" +
            "ping-period                               : 0\r\n" +
            "ping-timeout                              : 0\r\n" +
            "restart-schedule                          : \r\n\r\n";

        var records = _deserializer.Deserialize(output);
        var values = Assert.Single(records).Values;

        Assert.Equal(18, values.Count);
        Assert.Equal(
            "820d1955-349e-4173-9092-a3f206d328f7",
            values["cluster"]);
        Assert.Equal("WIN-P4BDRRBVMU8", values["HOST"]);
        Assert.Equal("1541", values["port"]);
        Assert.Equal("\"Локальный кластер\"", values["name"]);
        Assert.Equal("performance", values["load-balancing-mode"]);
        Assert.Equal("1", values["kill-problem-processes"]);
        Assert.Equal(string.Empty, values["restart-schedule"]);
    }

    [Fact]
    public void Deserialize_supports_multiple_records_and_colons_inside_values()
    {
        const string output =
            "cluster : first\n" +
            "description : primary:cluster\n" +
            "\n" +
            "cluster : second\r\n" +
            "description : secondary\r\n";

        var records = _deserializer.Deserialize(output);

        Assert.Equal(2, records.Count);
        Assert.Equal("first", records[0].Values["cluster"]);
        Assert.Equal("primary:cluster", records[0].Values["description"]);
        Assert.Equal("second", records[1].Values["cluster"]);
    }

    [Fact]
    public void Deserialize_empty_output_returns_no_records()
    {
        Assert.Empty(_deserializer.Deserialize(" \r\n\r\n"));
    }

    [Theory]
    [InlineData("not a key-value line")]
    [InlineData(": value")]
    [InlineData("key : first\nKEY : second")]
    public void Deserialize_rejects_malformed_records(string output)
    {
        Assert.Throws<RacOutputDeserializationException>(() =>
            _deserializer.Deserialize(output));
    }
}