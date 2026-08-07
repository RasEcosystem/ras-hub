using MudBlazor;

namespace RasHub.Web.Infrastructure.Themes.Definitions;

public static class CarbonTheme
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
                Black = "#111418",

                Primary = "#5D8FCF",
                Secondary = "#4B5D73",

                Info = "#74A9DC",
                Success = "#5F8F82",
                Warning = "#D1B06A",
                Error = "#A66873",

                Dark = "#1A1F25",

                Background = "#20252B",

                BackgroundGray = "#191E23",

                Surface = "#2A3037",

                DrawerBackground = "#252B31",
                AppbarBackground = "#1A1F24",

                AppbarText = "#D8DEE7",

                TextPrimary = "#D8DEE7",
                TextSecondary = "#A7B2BF",
                TextDisabled = "#697480",

                DrawerText = "#D0D8E2",
                DrawerIcon = "#91A0B1",

                ActionDefault = "#91A0B1",
                ActionDisabled = "#A1A9B2",
                ActionDisabledBackground = "#E2E6EA",

                LinesDefault = "#3A424B",
                LinesInputs = "#536170",

                TableLines = "#39414A",
                TableStriped = "#252B31",

                Divider = "#3A424A",
                DividerLight = "#30373F",

                Skeleton = "#353D46",

                OverlayLight = "#111418B8"
            }
        };
    }
}
