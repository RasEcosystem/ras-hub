using MudBlazor;

namespace RasHub.Web.Infrastructure.Themes.Definitions;

public static class SlateTheme
{
    public static AppThemeDefinition Create()
    {
        var mudTheme =
            GetMudThemeDefinition();

        var themeDefinition =
            new AppThemeDefinition(
                mudTheme);

        return themeDefinition;
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

                Secondary = "#4B5D73",
                SecondaryContrastText = "#FFFFFF",

                Info = "#6E8FA6",
                InfoContrastText = "#0C0E10",

                Success = "#72917E",
                SuccessContrastText = "#0C0E10",

                Warning = "#AE9564",
                WarningContrastText = "#0C0E10",

                Error = "#AA6870",
                ErrorContrastText = "#0C0E10",

                Dark = "#0C0E10",

                Background = "#0C0E10",

                BackgroundGray = "#090B0D",

                Surface = "#171B1F",

                DrawerBackground = "#13171B",

                AppbarBackground =
                    "rgba(12,14,16,.93)",

                AppbarText = "#E4E8ED",

                TextPrimary = "#D8DDE4",

                TextSecondary = "#96A0AD",

                TextDisabled = "#626B76",

                DrawerText = "#C0C7D0",

                DrawerIcon = "#9099A5",

                ActionDefault = "#B0B7C1",

                ActionDisabled = "#59616B",

                ActionDisabledBackground =
                    "#2A313844",

                LinesDefault = "#272E35",

                LinesInputs = "#3A434D",

                TableLines = "#272E35",

                TableStriped = "#14181C",

                Divider = "#232930",

                DividerLight = "#1A1E23",

                Skeleton = "#20252B",

                OverlayLight =
                    "#11161B88"
            }
        };
    }
}