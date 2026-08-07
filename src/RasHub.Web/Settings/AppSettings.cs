using Nava.Settings;

namespace RasHub.Web.Settings;

[SettingsKey("app-settings")]
public sealed class ApplicationSettings
{
    public AppTheme Theme { get; set; }
    public bool AllowForgotPassword { get; set; } = true;
    public bool AllowResendEmailConfirmation { get; set; } = true;
    public bool AllowPasskeyLogin { get; set; } = true;
    public bool AllowRegistration { get; set; } = true;
}