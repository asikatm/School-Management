using MudBlazor;
using P = SchoolManagement.Domain.Identity.Permissions;

namespace SchoolManagement.Web.Layout;

public static partial class NavCatalog
{
    public static NavGroup Academic { get; } = new("Academic setup", Icons.Material.Filled.AccountTree,
    [
        new NavItem("Campuses", "academic/campuses", Icons.Material.Filled.Domain, P.Settings.View, P.Settings.Manage),
        new NavItem("Academic years", "academic/years", Icons.Material.Filled.CalendarMonth, P.Settings.View, P.Settings.Manage),
        new NavItem("Programmes & classes", "academic/structure", Icons.Material.Filled.AccountTree, P.Settings.View, P.Settings.Manage),
        new NavItem("Sections", "academic/sections", Icons.Material.Filled.Groups, P.Settings.View, P.Settings.Manage),
        new NavItem("Shifts", "academic/shifts", Icons.Material.Filled.Schedule, P.Settings.View, P.Settings.Manage),
        new NavItem("Subjects, houses & fees", "academic/master-data", Icons.Material.Filled.MenuBook, P.Settings.View, P.Settings.Manage),
        new NavItem("Holidays", "academic/holidays", Icons.Material.Filled.EventBusy, P.Settings.View, P.Settings.Manage),
        new NavItem("Grading schemes", "academic/grading-schemes", Icons.Material.Filled.Grade, P.Settings.View, P.Settings.Manage),
        new NavItem("Curriculum", "academic/curriculum", Icons.Material.Filled.LibraryBooks, P.Academic.View),
        new NavItem("Rooms", "academic/rooms", Icons.Material.Filled.MeetingRoom, P.Academic.View),
        new NavItem("Bell schedule", "academic/bell-schedule", Icons.Material.Filled.NotificationsActive, P.Academic.View),
    ]);
}
