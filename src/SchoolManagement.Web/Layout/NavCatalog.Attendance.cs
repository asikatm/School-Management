using MudBlazor;
using P = SchoolManagement.Domain.Identity.Permissions;

namespace SchoolManagement.Web.Layout;

public static partial class NavCatalog
{
    public static NavGroup Attendance { get; } = new("Attendance & leave", Icons.Material.Filled.FactCheck,
    [
        new NavItem("Take attendance", "attendance", Icons.Material.Filled.HowToReg, P.Attendance.Take, P.Attendance.Edit),
        new NavItem("Today's overview", "attendance/overview", Icons.Material.Filled.Insights, P.Attendance.View),
        new NavItem("Monthly register", "attendance/monthly", Icons.Material.Filled.CalendarMonth, P.Attendance.View),
        new NavItem("Student leave", "attendance/leave", Icons.Material.Filled.EventBusy, P.Attendance.View),
        new NavItem("Attendance thresholds", "attendance/thresholds", Icons.Material.Filled.WarningAmber, P.Attendance.View),
        new NavItem("Staff attendance", "attendance/staff", Icons.Material.Filled.Badge, P.Attendance.Staff),
        new NavItem("Staff leave", "attendance/staff-leave", Icons.Material.Filled.BeachAccess, P.StaffLeave.Manage),
        new NavItem("My attendance & leave", "attendance/me", Icons.Material.Filled.Fingerprint, P.Attendance.View, P.Academic.View, P.Settings.View),
    ]);

    public static NavGroup Portal { get; } = new("Portal", Icons.Material.Filled.FamilyRestroom,
    [
        new NavItem("Student portal", "student", Icons.Material.Filled.School, P.Portal.Student, P.Portal.Parent),
        new NavItem("My children", "portal", Icons.Material.Filled.ChildCare, P.Portal.Parent),
        new NavItem("Homework, exams & results", "portal/learning", Icons.Material.Filled.AutoStories, P.Portal.Parent, P.Portal.Student),
    ]);
}
