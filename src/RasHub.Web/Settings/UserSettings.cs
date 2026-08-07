using Nava.Settings;

namespace RasHub.Web.Settings;

[SettingsKey("user-settings")]
public class UserSettings
{
    public AppTheme? Theme { get; set; }
}