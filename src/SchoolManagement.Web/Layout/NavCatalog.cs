using MudBlazor;
using SchoolManagement.Domain.Identity;

namespace SchoolManagement.Web.Layout;

/// <summary>A side-menu link, shown when the user holds any of <see cref="AnyOf"/> (empty = every signed-in user).</summary>
public sealed record NavItem(string Title, string Href, string Icon, params string[] AnyOf);

public sealed record NavGroup(string Title, string Icon, IReadOnlyList<NavItem> Items);

/// <summary>
/// The side menu. Each module keeps its links in its own NavCatalog.*.cs file; the order here is the menu order.
/// </summary>
public static partial class NavCatalog
{
    public static IReadOnlyList<NavGroup> Groups => [Admission, Students, Timetable, Academic, Administration];

    public static NavItem Dashboard { get; } = new("Dashboard", "", Icons.Material.Filled.Dashboard);
}
