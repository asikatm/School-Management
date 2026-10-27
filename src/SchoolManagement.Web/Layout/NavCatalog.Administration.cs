using MudBlazor;
using P = SchoolManagement.Domain.Identity.Permissions;

namespace SchoolManagement.Web.Layout;

public static partial class NavCatalog
{
    public static NavGroup Administration { get; } = new("Administration", Icons.Material.Filled.AdminPanelSettings,
    [
        // The approval inbox and personal notifications are open to every signed-in user.
        new NavItem("My approvals", "admin/approvals", Icons.Material.Filled.FactCheck),
        new NavItem("My notifications", "notifications", Icons.Material.Filled.Notifications),
        new NavItem("Notice board", "notices", Icons.Material.Filled.Campaign, P.Notices.Manage, P.Settings.View, P.Students.View),
        new NavItem("Approval requests", "admin/approval-requests", Icons.Material.Filled.ListAlt, P.Approvals.View),
        new NavItem("Approval workflows", "admin/approval-workflows", Icons.Material.Filled.AccountTree, P.Approvals.View),
        new NavItem("Users", "admin/users", Icons.Material.Filled.ManageAccounts, P.Users.View),
        new NavItem("Roles", "admin/roles", Icons.Material.Filled.Security, P.Users.View),
        new NavItem("Message outbox", "admin/notifications/outbox", Icons.Material.Filled.Outbox, P.Notifications.View),
        new NavItem("Message templates", "admin/notifications/templates", Icons.Material.Filled.Article, P.Notifications.View),
        new NavItem("Audit trail", "admin/audit", Icons.Material.Filled.History, P.Audit.View),
        new NavItem("Settings", "admin/settings", Icons.Material.Filled.Settings, P.Settings.View),
        new NavItem("Backups", "admin/backups", Icons.Material.Filled.Backup, P.Backup.Manage),
    ]);
}
