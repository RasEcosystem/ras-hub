using RasHub.Infrastructure.RasGates.Rac.Infobases.Deserialization;
using RasHub.Infrastructure.RasGates.Rac.Parsing;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac.Infobases.Deserialization;

public sealed class RacInfobaseOutputV1DeserializerTests
{
    private readonly RacInfobaseOutputV1Deserializer _deserializer = new(
        new RacKeyValueOutputDeserializer());

    [Fact]
    public void Deserialize_summary_list_maps_infobases_and_empty_description()
    {
        const string output =
            "infobase : 85f82b58-d02c-4f40-9ad3-2131adf31e48\r\n" +
            "name     : rim_next\r\n" +
            "descr    : \r\n" +
            "\r\n" +
            "infobase : e2499a98-90c4-48c5-b1e1-8320fca8c6f1\r\n" +
            "name     : \"rim_demo\"\r\n" +
            "descr    : \"Demo database\"\r\n";

        var infobases = _deserializer.Deserialize(output);

        Assert.Collection(
            infobases,
            infobase =>
            {
                Assert.Equal(
                    Guid.Parse("85f82b58-d02c-4f40-9ad3-2131adf31e48"),
                    infobase.ExternalId);
                Assert.Equal("rim_next", infobase.Name);
                Assert.Equal(string.Empty, infobase.Description);
            },
            infobase =>
            {
                Assert.Equal(
                    Guid.Parse("e2499a98-90c4-48c5-b1e1-8320fca8c6f1"),
                    infobase.ExternalId);
                Assert.Equal("rim_demo", infobase.Name);
                Assert.Equal("Demo database", infobase.Description);
            });
    }

    [Fact]
    public void Deserialize_missing_description_rejects_record()
    {
        const string output =
            "infobase : 85f82b58-d02c-4f40-9ad3-2131adf31e48\n" +
            "name : rim_next\n";

        Assert.Throws<RacOutputDeserializationException>(() =>
            _deserializer.Deserialize(output));
    }

    [Fact]
    public void Deserialize_invalid_infobase_id_rejects_record()
    {
        const string output =
            "infobase : invalid\n" +
            "name : rim_next\n" +
            "descr : \n";

        Assert.Throws<RacOutputDeserializationException>(() =>
            _deserializer.Deserialize(output));
    }

    [Fact]
    public void Deserialize_duplicate_infobase_id_rejects_output()
    {
        const string output =
            "infobase : 85f82b58-d02c-4f40-9ad3-2131adf31e48\n" +
            "name : first\n" +
            "descr : \n" +
            "\n" +
            "infobase : 85f82b58-d02c-4f40-9ad3-2131adf31e48\n" +
            "name : second\n" +
            "descr : \n";

        Assert.Throws<RacOutputDeserializationException>(() =>
            _deserializer.Deserialize(output));
    }
}
