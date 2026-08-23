namespace RasHub.Web;

internal static class RasHubVersion
{
    public const string Informational = ThisAssembly.AssemblyInformationalVersion;

    public const string Display = ThisAssembly.NuGetPackageVersion;

    public static string? PrereleaseLabel { get; } = GetPrereleaseLabel(Display);

    private static string? GetPrereleaseLabel(string version)
    {
        var prereleaseIndex = version.IndexOf('-', StringComparison.Ordinal);

        if (prereleaseIndex < 0 || prereleaseIndex == version.Length - 1)
        {
            return null;
        }

        var labelStart = prereleaseIndex + 1;
        var labelEnd = version.IndexOf('.', labelStart);
        var label = labelEnd < 0
            ? version[labelStart..]
            : version[labelStart..labelEnd];

        return label.ToUpperInvariant();
    }
}
