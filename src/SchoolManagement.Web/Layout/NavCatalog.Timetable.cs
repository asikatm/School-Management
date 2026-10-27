using MudBlazor;
using P = SchoolManagement.Domain.Identity.Permissions;

namespace SchoolManagement.Web.Layout;

public static partial class NavCatalog
{
    public static NavGroup Timetable { get; } = new("Timetable & calendar", Icons.Material.Filled.CalendarViewWeek,
    [
        new NavItem("My day", "timetable/my-day", Icons.Material.Filled.Today, P.Academic.View),
        new NavItem("Timetable", "timetable", Icons.Material.Filled.GridOn, P.Academic.View),
        new NavItem("Teacher allocation", "timetable/allocation", Icons.Material.Filled.AssignmentInd, P.Academic.View),
        new NavItem("Substitutions", "timetable/substitutions", Icons.Material.Filled.SwapHoriz, P.Academic.Substitutions),
        // The calendar is for everyone who signs in.
        new NavItem("Academic calendar", "timetable/calendar", Icons.Material.Filled.Event),
    ]);
}
