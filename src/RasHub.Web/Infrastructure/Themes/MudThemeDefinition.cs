using MudBlazor;

namespace RasHub.Web.Infrastructure.Themes;

public class MudThemeDefinition
{
    public required Palette Palette { get; set; }
    public Shadow Shadows { get; set; } = CreateShadows();
    public Typography Typography { get; set; } = CreateTypography();
    public bool IsDarkMode { get; set; }

    private static Typography CreateTypography()
    {
        return new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily =
                [
                    "Inter",
                    "Roboto",
                    "Segoe UI",
                    "Arial",
                    "sans-serif"
                ]
            },
            Button = new ButtonTypography
            {
                TextTransform = "none"
            }
        };
    }

    private static Shadow CreateShadows()
    {
        var shadows = new Shadow();

        shadows.Elevation[1] =
            "0 0 0 1px rgba(151,164,180,.06), " +
            "0 2px 6px rgba(0,0,0,.38)";

        shadows.Elevation[2] =
            "0 0 0 1px rgba(151,164,180,.08), " +
            "0 5px 12px rgba(0,0,0,.44)";

        shadows.Elevation[3] =
            "0 0 0 1px rgba(151,164,180,.10), " +
            "0 9px 20px rgba(0,0,0,.50)";

        return shadows;
    }
}
