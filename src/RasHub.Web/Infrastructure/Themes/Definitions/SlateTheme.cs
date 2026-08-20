using MudBlazor;

namespace RasHub.Web.Infrastructure.Themes.Definitions;

public static class SlateTheme
{
    public static AppThemeDefinition Create()
    {
        return new AppThemeDefinition(GetMudThemeDefinition());
    }

    private static MudThemeDefinition GetMudThemeDefinition()
    {
        return new MudThemeDefinition
        {
            IsDarkMode = true,
            Palette = new PaletteDark
            {
                Black = "#0A0B0C",
                Primary = "#5D8FCF",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#4B5D73",
                SecondaryContrastText = "#FFFFFF",
                Info = "#6E8FA6",
                InfoContrastText = "#FFFFFF",
                Success = "#72917E",
                SuccessContrastText = "#FFFFFF",
                Warning = "#AE9564",
                WarningContrastText = "#FFFFFF",
                Error = "#AA6870",
                ErrorContrastText = "#FFFFFF",
                Dark = "#0C0E10",
                Background = "#0C0E10",
                BackgroundGray = "#090B0D",
                Surface = "#171B1F",
                DrawerBackground = "#13171B",
                AppbarBackground = "rgba(12,14,16,.93)",
                AppbarText = "#E4E8ED",
                TextPrimary = "#D8DDE4",
                TextSecondary = "#96A0AD",
                TextDisabled = "#626B76",
                DrawerText = "#C0C7D0",
                DrawerIcon = "#9099A5",
                ActionDefault = "#B0B7C1",
                ActionDisabled = "#59616B",
                ActionDisabledBackground = "#2A313844",
                LinesDefault = "#272E35",
                LinesInputs = "#3A434D",
                TableLines = "#272E35",
                TableStriped = "#14181C",
                Divider = "#232930",
                DividerLight = "#1A1E23",
                Skeleton = "#20252B",
                OverlayLight = "#11161B88"
            }
        };
    }
}