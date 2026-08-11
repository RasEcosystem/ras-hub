using RasHub.Web.Infrastructure.Themes.Definitions;
using RasHub.Web.Settings;

namespace RasHub.Web.Infrastructure.Themes.Providers;

public class ThemeProvider
{
    public static AppThemeDefinition GetTheme(
        AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Carbon => CarbonTheme.Create(),
            AppTheme.Slate => SlateTheme.Create(),
            AppTheme.Light => LightTheme.Create(),
            AppTheme.System => CarbonTheme.Create(),
            _ => CarbonTheme.Create()
        };
    }
}
