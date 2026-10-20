namespace SchoolManagement.Domain.Identity;

/// <summary>
/// Permission codes are "module.action" (CORE-03): view / create / edit / approve / export per module.
/// </summary>
public static class Permissions
{
    public static class Settings
    {
        public const string View = "settings.view";
        public const string Manage = "settings.manage";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Manage = "users.manage";
    }

    public static class Audit
    {
        public const string View = "audit.view";
        public const string Export = "audit.export";
    }

    public static class Approvals
    {
        public const string View = "approvals.view";
        public const string Configure = "approvals.configure";
    }

    public static class Notifications
    {
        public const string View = "notifications.view";
        public const string Send = "notifications.send";
        public const string ManageTemplates = "notifications.templates";
    }

    public static class Admission
    {
        public const string View = "admission.view";
        public const string Create = "admission.create";
        public const string Edit = "admission.edit";
        public const string Approve = "admission.approve";
        public const string Export = "admission.export";
    }

    public static class Students
    {
        public const string View = "students.view";
        public const string Create = "students.create";
        public const string Edit = "students.edit";
        public const string Approve = "students.approve";
        public const string Export = "students.export";
        public const string Documents = "students.documents";
    }

    public static class Backup
    {
        public const string Manage = "backup.manage";
    }

    public static IReadOnlyList<string> All { get; } =
        typeof(Permissions).GetNestedTypes()
            .SelectMany(t => t.GetFields())
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(p => p)
            .ToList();
}

/// <summary>Default role names from CORE-03.</summary>
public static class RoleNames
{
    public const string ItAdmin = "IT Admin";
    public const string Principal = "Principal";
    public const string VicePrincipal = "Vice-Principal";
    public const string HeadOfSection = "Head of Section";
    public const string ClassTeacher = "Class Teacher";
    public const string SubjectTeacher = "Subject Teacher";
    public const string Accountant = "Accountant";
    public const string AdmissionOfficer = "Admission Officer";
    public const string Librarian = "Librarian";
    public const string TransportInCharge = "Transport In-charge";
    public const string Parent = "Parent";
    public const string Student = "Student";
}
