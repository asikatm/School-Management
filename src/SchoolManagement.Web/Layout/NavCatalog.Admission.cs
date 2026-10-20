using MudBlazor;
using P = SchoolManagement.Domain.Identity.Permissions;

namespace SchoolManagement.Web.Layout;

public static partial class NavCatalog
{
    public static NavGroup Admission { get; } = new("Admission", Icons.Material.Filled.HowToReg,
    [
        new NavItem("Enquiries", "admission/enquiries", Icons.Material.Filled.ContactPhone, P.Admission.View),
        new NavItem("My follow-ups", "admission/tasks", Icons.Material.Filled.TaskAlt, P.Admission.View),
        new NavItem("Applications", "admission/applications", Icons.Material.Filled.Assignment, P.Admission.View),
        new NavItem("Admission tests", "admission/tests", Icons.Material.Filled.Quiz, P.Admission.View),
        new NavItem("Seats & selection", "admission/selection", Icons.Material.Filled.EventSeat, P.Admission.View),
        new NavItem("Offers", "admission/offers", Icons.Material.Filled.MarkEmailRead, P.Admission.View),
        new NavItem("Admission invoices", "admission/invoices", Icons.Material.Filled.ReceiptLong, P.Admission.View),
        new NavItem("Enquiry analytics", "admission/analytics", Icons.Material.Filled.Insights, P.Admission.View),
    ]);
}
