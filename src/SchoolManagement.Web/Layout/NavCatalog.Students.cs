using MudBlazor;
using P = SchoolManagement.Domain.Identity.Permissions;

namespace SchoolManagement.Web.Layout;

public static partial class NavCatalog
{
    public static NavGroup Students { get; } = new("Students", Icons.Material.Filled.School,
    [
        new NavItem("All students", "students", Icons.Material.Filled.People, P.Students.View),
        new NavItem("Import", "students/import", Icons.Material.Filled.UploadFile, P.Students.Create),
        new NavItem("Sections & rolls", "students/allocation", Icons.Material.Filled.GroupWork, P.Students.Edit),
        new NavItem("Subject choices", "students/subject-choices", Icons.Material.Filled.Checklist, P.Academic.View),
        new NavItem("Promotion", "students/promotion", Icons.Material.Filled.TrendingUp, P.Students.Approve),
        new NavItem("Withdrawals", "students/withdrawals", Icons.Material.Filled.ExitToApp, P.Students.View),
        new NavItem("TC register", "students/transfer-certificates", Icons.Material.Filled.Description, P.Students.View),
        new NavItem("ID cards", "students/id-cards", Icons.Material.Filled.CreditCard, P.Students.View),
        new NavItem("Expiring documents", "students/documents/expiring", Icons.Material.Filled.EventBusy, P.Students.View),
    ]);
}
