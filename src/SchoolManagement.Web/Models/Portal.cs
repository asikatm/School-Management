using SchoolManagement.Domain.Attendance;

namespace SchoolManagement.Web.Models;

// Mirrors SchoolManagement.Api/Modules/Communication (notice board) and the student dashboard in FamilyPortalController.

public record NoticeView(Guid Id, Guid? CampusId, string? CampusName, Guid? YearGroupId, string? ClassName, string Title, string? TitleBn, string Body,
    bool ForStudents, bool ForParents, bool ForStaff, bool IsPinned, bool IsUrgent, DateTime PublishedAtUtc, DateOnly? ExpiresOn, bool HasAttachment,
    string? AttachmentName, string? PostedBy, bool Active, bool CanEdit);

public record TermAttendance(string? TermName, DateOnly? From, DateOnly? To, int Present, int Absent, int Late, int Leave, decimal? Percent);

public record PortalTimetable(string? SectionLabel, string? ClassTeacher, IReadOnlyList<DayOfWeek> Days, IReadOnlyList<PeriodView> Periods, IReadOnlyList<SlotView> Slots);

public record PortalOverview(Guid StudentId, string Name, string? NameBn, string AdmissionNo, string? ClassName, int? RollNo, string CampusName, bool HasPhoto,
    string? BloodGroup, string? House, AttendanceMark? Today, TermAttendance Term, int HomeworkPending, IReadOnlyList<SlotView> TodayClasses,
    PortalExam? NextExam, StudentResultSummary? LatestResult, IReadOnlyList<NoticeView> LatestNotices);
