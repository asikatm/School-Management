using MudBlazor;
using P = SchoolManagement.Domain.Identity.Permissions;

namespace SchoolManagement.Web.Layout;

public static partial class NavCatalog
{
    public static NavGroup Learning { get; } = new("Teaching & learning", Icons.Material.Filled.MenuBook,
    [
        new NavItem("Homework", "learning/homework", Icons.Material.Filled.Assignment, P.Learning.View),
        new NavItem("Class diary", "learning/diary", Icons.Material.Filled.EditNote, P.Learning.View),
        new NavItem("Learning resources", "learning/resources", Icons.Material.Filled.LocalLibrary, P.Learning.View),
        new NavItem("Gradebook", "learning/gradebook", Icons.Material.Filled.Grading, P.Learning.View),
        new NavItem("Gradebook moderation", "learning/moderation", Icons.Material.Filled.FactCheck, P.Learning.Moderate),
    ]);

    public static NavGroup Exams { get; } = new("Examinations", Icons.Material.Filled.Quiz,
    [
        new NavItem("Exams & results", "exams", Icons.Material.Filled.EventNote, P.Exams.View),
        new NavItem("My mark sheets", "exams/my-marks", Icons.Material.Filled.EditNote, P.Exams.Marks),
        new NavItem("Transcripts", "exams/transcripts", Icons.Material.Filled.WorkspacePremium, P.Exams.View),
    ]);
}
