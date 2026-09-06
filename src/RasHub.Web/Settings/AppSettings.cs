using Nava.Settings;

namespace RasHub.Web.Settings;

[SettingsKey("app-settings")]
public sealed class ApplicationSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Hub;

    public bool DebugMode { get; set; }
}
