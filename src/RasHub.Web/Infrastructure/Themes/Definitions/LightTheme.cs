using MudBlazor;

namespace RasHub.Web.Infrastructure.Themes.Definitions;

public static class LightTheme
{
    public static AppThemeDefinition Create()
    {
        return new AppThemeDefinition(
            new MudThemeDefinition
            {
                IsDarkMode = false,
                Shadows = CreateShadows(),
                Palette = new PaletteLight
                {
                    Black = "#172033",
                    Primary = "#4D75A7",
                    PrimaryContrastText = "#FFFFFF",
                    Secondary = "#7A899C",
                    SecondaryContrastText = "#FFFFFF",
                    Info = "#78A9BD",
                    InfoContrastText = "#FFFFFF",
                    Success = "#79A690",
                    SuccessContrastText = "#FFFFFF",
                    Warning = "#D8AE68",
                    WarningContrastText = "#FFFFFF",
                    Error = "#D27B87",
                    ErrorContrastText = "#FFFFFF",
                    Dark = "#253044",
                    Background = "#F5F6F8",
                    BackgroundGray = "#EBEEF2",
                    Surface = "#FFFFFF",
                    DrawerBackground = "#FFFFFF",
                    AppbarBackground = "rgba(255,255,255,.96)",
                    AppbarText = "#253044",
                    TextPrimary = "#253044",
                    TextSecondary = "#657184",
                    TextDisabled = "#929BA8",
                    DrawerText = "#3C485A",
                    DrawerIcon = "#718096",
                    ActionDefault = "#718096",
                    LinesDefault = "#D9DEE6",
                    LinesInputs = "#B5BECA",
                    TableLines = "#E0E4EA",
                    TableStriped = "#F7F8FA",
                    Divider = "#E0E4EA",
                    DividerLight = "#ECEFF3",
                    Skeleton = "#E5E9EE",
                    OverlayLight = "#FFFFFFB8"
                }
            });
    }

    private static Shadow CreateShadows()
    {
        var shadows = new Shadow();

        shadows.Elevation[1] =
            "0 1px 2px rgba(15,23,42,.06), 0 1px 4px rgba(15,23,42,.05)";
        shadows.Elevation[2] =
            "0 2px 6px rgba(15,23,42,.08), 0 6px 16px rgba(15,23,42,.06)";
        shadows.Elevation[3] =
            "0 4px 10px rgba(15,23,42,.10), 0 10px 24px rgba(15,23,42,.08)";

        return shadows;
    }
}
