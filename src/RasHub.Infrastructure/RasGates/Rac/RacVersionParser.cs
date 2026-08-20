using System.Globalization;
using System.Text.RegularExpressions;
using RasHub.Application.RasGates.Exceptions;

namespace RasHub.Infrastructure.RasGates.Rac;

public sealed partial class RacVersionParser
{
    public Version Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw InvalidVersion();

        var matches = VersionPattern().Matches(value);

        if (matches.Count != 1)
            throw InvalidVersion();

        var match = matches[0];
        var major = ParseComponent(match.Groups[1].Value);
        var minor = ParseComponent(match.Groups[2].Value);
        var build = ParseComponent(match.Groups[3].Value);
        var revision = match.Groups[4].Success
            ? ParseComponent(match.Groups[4].Value)
            : 0;

        return new Version(major, minor, build, revision);
    }

    private static int ParseComponent(string value)
    {
        if (int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var component))
            return component;

        throw InvalidVersion();
    }

    private static RasGateClientException InvalidVersion()
    {
        return new RasGateClientException(
            "RasGate returned an invalid RAC version.");
    }

    [GeneratedRegex(
        @"(?<!\d)(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
