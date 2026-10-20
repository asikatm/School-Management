using MudBlazor;

namespace SchoolManagement.Web.Layout;

public static class AppTheme
{
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1E4E8C",
            Secondary = "#E0A100",
            AppbarBackground = "#1E4E8C",
            Background = "#F5F7FA",
            DrawerBackground = "#FFFFFF"
        },
        LayoutProperties = new LayoutProperties { DrawerWidthLeft = "260px" }
    };
}
