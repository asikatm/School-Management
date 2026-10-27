using SchoolManagement.Domain.Attendance;

namespace SchoolManagement.Web.Models;

// Mirrors SchoolManagement.Api/Modules/Attendance (ATT: student & staff attendance, leave) and the parent portal.

// ---- Student registers
public record RegisterStudent(Guid StudentId, string AdmissionNo, string Name, int? RollNo, AttendanceMark? Mark, TimeOnly? ArrivalTime, string? Remarks,
    AttendanceSource? Source, bool OnApprovedLeave);
public record RegisterPeriod(int PeriodNo, string Name, TimeOnly StartTime, TimeOnly EndTime, string? SubjectName, bool Taken);
public record RegisterView(Guid SectionId, string SectionLabel, Guid CampusId, DateOnly Date, int? PeriodNo, string? ClosedReason, bool Taken, string? TakenBy,
    DateTime? TakenAtUtc, bool CanEdit, string? EditBlockedReason, bool PeriodWise, IReadOnlyList<RegisterPeriod> Periods, IReadOnlyList<RegisterStudent> Students);
public record MarkEntry(Guid StudentId, AttendanceMark Mark, TimeOnly? ArrivalTime, string? Remarks);
public record SaveRegisterRequest(Guid SectionId, DateOnly Date, int? PeriodNo, List<MarkEntry> Entries);
public record RegisterSummary(Guid SectionId, string SectionLabel, int? PeriodNo, int Present, int Absent, int Late, int OnLeave, string? TakenBy, DateTime TakenAtUtc);
public record PendingRegister(Guid SectionId, string SectionLabel, string? ClassTeacher, int Students);
public record DailyOverview(DateOnly Date, string? ClosedReason, int Sections, int Taken, int Present, int Absent, int Late, int OnLeave,
    IReadOnlyList<PendingRegister> NotTaken, IReadOnlyList<RegisterSummary> Registers);
public record MonthlyRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, IReadOnlyList<string> Days, int Present, int Absent, int Late, int Leave, decimal Percent);
public record MonthlyRegister(Guid SectionId, string SectionLabel, int Year, int Month, IReadOnlyList<DateOnly> Days, IReadOnlyList<bool> SchoolDays, IReadOnlyList<MonthlyRow> Students);
public record ThresholdRow(Guid StudentId, Guid CampusId, string AdmissionNo, string Name, string ClassName, int Marked, int Attended, int Absent, int Leave,
    decimal Percent, WarningLevel? Level, DateTime? WarnedAtUtc);
public record ThresholdReport(Guid TermId, string TermName, DateOnly From, DateOnly To, decimal WarningPercent, decimal MinimumPercent, IReadOnlyList<ThresholdRow> Students);

// ---- Student leave
public record StudentLeaveView(Guid Id, Guid StudentId, string AdmissionNo, string StudentName, string? ClassName, Guid CampusId, DateOnly FromDate, DateOnly ToDate,
    int SchoolDays, string Reason, bool HasAttachment, string? AttachmentName, LeaveStatus Status, string? RequestedBy, DateTime RequestedAtUtc,
    string? DecidedBy, DateTime? DecidedAtUtc, string? DecisionNote, bool CanDecide);
public record LeaveDecisionRequest(bool Approve, string? Note);

// ---- Staff attendance
public record PunchRequest(Guid? CampusId, double? Latitude, double? Longitude);
public record GeoPosition(double Latitude, double Longitude, double Accuracy);
public record StaffDayView(Guid UserId, string Name, Guid CampusId, DateOnly Date, StaffAttendanceStatus? Status, DateTime? CheckInUtc, DateTime? CheckOutUtc,
    AttendanceSource? Source, bool? WithinGeofence, int LateMinutes, int EarlyLeaveMinutes, int OvertimeMinutes, string? LeaveType, string? Note);
public record StaffCorrection(Guid UserId, Guid CampusId, DateOnly Date, StaffAttendanceStatus Status, TimeOnly? CheckIn, TimeOnly? CheckOut, string? Note);
public record StaffMonthlyRow(Guid UserId, string Name, int SchoolDays, int Present, int Absent, int Leave, int LateDays, int LateMinutes, int EarlyLeaveMinutes, int OvertimeMinutes);

// ---- Staff leave
public record LeaveTypeRequest(string Code, string Name, string? NameBn, decimal DaysPerYear, bool IsPaid, bool RequiresDocument, bool IsActive);
public record LeaveTypeView(Guid Id, string Code, string Name, string? NameBn, decimal DaysPerYear, bool IsPaid, bool RequiresDocument, bool IsActive);
public record LeaveBalance(Guid LeaveTypeId, string Code, string Name, bool Unlimited, decimal Entitled, decimal Taken, decimal Pending, decimal Remaining);
public record StaffLeaveApply(Guid LeaveTypeId, DateOnly FromDate, DateOnly ToDate, bool HalfDay, string Reason, Guid? CampusId);
public record StaffLeaveView(Guid Id, Guid UserId, string Name, Guid CampusId, Guid LeaveTypeId, string LeaveTypeCode, string LeaveType, DateOnly FromDate, DateOnly ToDate,
    bool HalfDay, decimal Days, string Reason, LeaveStatus Status, Guid? ApprovalRequestId, string? DecisionNote, DateTime RequestedAtUtc);

// ---- Parent portal
public record ChildSummary(Guid StudentId, string AdmissionNo, string Name, string? ClassName, Guid CampusId, AttendanceMark? Today, string? TermName,
    decimal? TermPercent, int TermAbsent, int PendingLeave);
public record ChildDay(DateOnly Date, string Code, bool SchoolDay, TimeOnly? ArrivalTime, string? Remarks);
public record ChildMonth(Guid StudentId, int Year, int Month, IReadOnlyList<ChildDay> Days, int Present, int Absent, int Late, int Leave, decimal Percent);
