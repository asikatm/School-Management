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

    public static class Attendance
    {
        public const string View = "attendance.view";

        /// <summary>Take the register for one's own classes (class teacher, or teaching the section).</summary>
        public const string Take = "attendance.take";

        /// <summary>Take or change any section's register, including after the edit window.</summary>
        public const string Edit = "attendance.edit";

        /// <summary>Decide students' leave applications for any section (class teachers can always decide their own).</summary>
        public const string ApproveLeave = "attendance.leave";

        /// <summary>See and correct staff attendance.</summary>
        public const string Staff = "attendance.staff";
    }

    public static class StaffLeave
    {
        /// <summary>Leave types, entitlements and every staff member's requests and balances.</summary>
        public const string Manage = "staffleave.manage";
    }

    public static class Portal
    {
        /// <summary>Guardian self-service for their own children (PRT).</summary>
        public const string Parent = "portal.parent";

        /// <summary>A student's own homework, resources, timetable and published results (PRT).</summary>
        public const string Student = "portal.student";
    }

    public static class Notices
    {
        /// <summary>Post, change and remove notices on the notice board.</summary>
        public const string Manage = "notices.manage";
    }

    public static class Learning
    {
        /// <summary>See homework, class diaries, resources and gradebooks.</summary>
        public const string View = "learning.view";

        /// <summary>Set homework, write the diary, share resources and keep the gradebook for one's own classes.</summary>
        public const string Teach = "learning.teach";

        /// <summary>Do the above for any class, and set gradebook categories and weights.</summary>
        public const string Manage = "learning.manage";

        /// <summary>Moderate and approve term grades (head of department).</summary>
        public const string Moderate = "learning.moderate";
    }

    public static class Exams
    {
        public const string View = "exams.view";

        /// <summary>Exam office: schedules, date sheets, seating, admit cards, finalising and publishing results.</summary>
        public const string Manage = "exams.manage";

        /// <summary>Enter marks for the subjects one teaches, and report-card remarks as class teacher.</summary>
        public const string Marks = "exams.marks";

        /// <summary>Verify, moderate, approve and re-open mark sheets of any section.</summary>
        public const string Moderate = "exams.moderate";
    }

    public static class Academic
    {
        /// <summary>See curriculum, allocations, timetables and the calendar.</summary>
        public const string View = "academic.view";

        /// <summary>Curriculum, subject groups, rooms, bell schedule and teacher allocation.</summary>
        public const string Manage = "academic.manage";
        public const string Timetable = "academic.timetable";
        public const string Substitutions = "academic.substitutions";
        public const string Calendar = "academic.calendar";
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
    public const string ExamOfficer = "Exam Officer";
    public const string Librarian = "Librarian";
    public const string TransportInCharge = "Transport In-charge";
    public const string Parent = "Parent";
    public const string Student = "Student";
}
