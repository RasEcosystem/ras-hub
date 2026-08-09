using RasHub.Application.RasGates.Exceptions;
using RasHub.Infrastructure.RasGates.Rac;

namespace RasHub.Infrastructure.UnitTests.RasGates.Rac;

public sealed class RacVersionParserTests
{
    private readonly RacVersionParser _parser = new();

    [Theory]
    [InlineData("8.3.27.2214", 8, 3, 27, 2214)]
    [InlineData("rac 8.3.27.2214", 8, 3, 27, 2214)]
    [InlineData("Remote Administrative Client 8.3.27", 8, 3, 27, 0)]
    public void Parse_valid_output_returns_normalized_version(
        string output,
        int major,
        int minor,
        int build,
        int revision)
    {
        var version = _parser.Parse(output);

        Assert.Equal(new Version(major, minor, build, revision), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("rac 8.3.27.1 runtime 8.3.28.1")]
    public void Parse_ambiguous_or_invalid_output_rejects_version(string output)
    {
        Assert.Throws<RasGateClientException>(() => _parser.Parse(output));
    }
}