using MudBlazor;

namespace SchoolManagement.Web.Layout;

public static class AppTheme
{
    private static readonly string[] Font = ["Inter", "Noto Sans Bengali", "Segoe UI", "Roboto", "sans-serif"];

    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1E4E8C",
            Secondary = "#E0A100",
            Tertiary = "#00ACC1",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#1B2A41",
            Background = "#F4F6FB",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#3C4A60",
            DrawerIcon = "#6B7A90",
            TextPrimary = "#1B2A41",
            TextSecondary = "#6B7A90",
            LinesDefault = "#E6EAF2",
            TableLines = "#EEF1F6",
            Divider = "#E6EAF2"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Font },
            H4 = new H4Typography { FontFamily = Font, FontWeight = "700", FontSize = "1.85rem", LetterSpacing = "-.02em" },
            H5 = new H5Typography { FontFamily = Font, FontWeight = "700" },
            H6 = new H6Typography { FontFamily = Font, FontWeight = "600" },
            Button = new ButtonTypography { FontFamily = Font, FontWeight = "600", TextTransform = "none" }
        },
        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft = "264px",
            DefaultBorderRadius = "10px",
            AppbarHeight = "64px"
        }
    };
}
