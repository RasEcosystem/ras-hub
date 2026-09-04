namespace RasHub.Web;

internal static class RasHubVersion
{
    public const string Informational = ThisAssembly.AssemblyInformationalVersion;

    public static string Display { get; } = GetDisplayVersion(
        ThisAssembly.NuGetPackageVersion);

    public static string? PrereleaseLabel { get; } = GetPrereleaseLabel(Display);

    private static string? GetPrereleaseLabel(string version)
    {
        var prereleaseIndex = version.IndexOf('-', StringComparison.Ordinal);

        if (prereleaseIndex < 0 || prereleaseIndex == version.Length - 1) return null;

        var labelStart = prereleaseIndex + 1;
        var labelEnd = version.IndexOf('.', labelStart);
        var label = labelEnd < 0
            ? version[labelStart..]
            : version[labelStart..labelEnd];

        return label.ToUpperInvariant();
    }

    private static string GetDisplayVersion(string version)
    {
        var gitSuffixIndex = version.LastIndexOf(".g", StringComparison.Ordinal);

        if (gitSuffixIndex < 0)
            gitSuffixIndex = version.LastIndexOf("-g", StringComparison.Ordinal);

        if (gitSuffixIndex < 0 || gitSuffixIndex == version.Length - 2) return version;

        var gitRevision = version.AsSpan(gitSuffixIndex + 2);

        foreach (var character in gitRevision)
            if (!Uri.IsHexDigit(character))
                return version;

        return version[..gitSuffixIndex];
    }
}
