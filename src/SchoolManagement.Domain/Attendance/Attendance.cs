using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Attendance;

public enum AttendanceMark
{
    Present = 0,
    Absent = 1,
    Late = 2,
    Leave = 3
}

public enum AttendanceSource
{
    Manual = 0,
    Leave = 1,
    Device = 2
}

/// <summary>ATT-01: a student's daily attendance. One row per student per day; every change is audited.</summary>
[Audited]
public class StudentAttendance : CampusRecord, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid StudentId { get; set; }
    public Guid SectionId { get; set; }
    public DateOnly Date { get; set; }
    public AttendanceMark Mark { get; set; }
    public TimeOnly? ArrivalTime { get; set; }
    public string? Remarks { get; set; }
    public AttendanceSource Source { get; set; }
    public Guid? StudentLeaveRequestId { get; set; }

    /// <summary>ATT-03: when the absence/late alert went to the guardian; null until sent (alerts go once a day).</summary>
    public DateTime? AlertSentAtUtc { get; set; }
}

/// <summary>ATT-01: period-wise attendance for senior years.</summary>
[Audited]
public class PeriodAttendance : CampusRecord, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid StudentId { get; set; }
    public Guid SectionId { get; set; }
    public DateOnly Date { get; set; }
    public int PeriodNo { get; set; }
    public Guid? SubjectId { get; set; }
    public AttendanceMark Mark { get; set; }
}

/// <summary>Who took a section's register and when (daily when <see cref="PeriodNo"/> is null). Drives the edit window and the "not taken yet" list.</summary>
public class AttendanceRegister : CampusRecord, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid SectionId { get; set; }
    public DateOnly Date { get; set; }
    public int? PeriodNo { get; set; }
    public Guid? TakenByUserId { get; set; }
    public DateTime TakenAtUtc { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int OnLeave { get; set; }
}

public enum LeaveStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}

/// <summary>ATT-04: a guardian's leave application for a child; the class teacher decides.</summary>
[Audited]
public class StudentLeaveRequest : CampusEntity
{
    public Guid StudentId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public required string Reason { get; set; }
    public string? AttachmentPath { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentType { get; set; }
    public LeaveStatus Status { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionNote { get; set; }
}

public enum WarningLevel
{
    /// <summary>Below the warning line (e.g. 85%).</summary>
    Caution = 0,

    /// <summary>Below the exam-eligibility minimum (e.g. 75%).</summary>
    BelowMinimum = 1
}

/// <summary>ATT-05: a threshold warning already sent, so each level is sent once per student per term.</summary>
public class AttendanceWarning : CampusRecord
{
    public Guid StudentId { get; set; }
    public Guid TermId { get; set; }
    public WarningLevel Level { get; set; }
    public decimal Percent { get; set; }
    public DateTime SentAtUtc { get; set; }
}

public enum StaffAttendanceStatus
{
    Present = 0,
    Absent = 1,
    Leave = 2,
    Holiday = 3
}

/// <summary>ATT-06: a staff member's day — punches, lateness, early leaving and overtime.</summary>
[Audited]
public class StaffAttendance : CampusRecord
{
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public StaffAttendanceStatus Status { get; set; }
    public DateTime? CheckInUtc { get; set; }
    public DateTime? CheckOutUtc { get; set; }
    public AttendanceSource Source { get; set; }
    public double? CheckInLatitude { get; set; }
    public double? CheckInLongitude { get; set; }
    public bool? WithinGeofence { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public Guid? StaffLeaveRequestId { get; set; }
    public string? Note { get; set; }
}

/// <summary>ATT-07: a kind of staff leave with its yearly entitlement.</summary>
[Audited]
public class LeaveType : SoftDeleteEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }

    /// <summary>Days allowed per academic year; 0 means no limit (e.g. unpaid leave).</summary>
    public decimal DaysPerYear { get; set; }
    public bool IsPaid { get; set; } = true;
    public bool RequiresDocument { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>ATT-07: a per-person override of a leave type's entitlement for one year (e.g. pro-rata for a new joiner).</summary>
public class LeaveEntitlement : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public Guid AcademicYearId { get; set; }
    public decimal Days { get; set; }
}

/// <summary>ATT-07: a staff leave request, routed through the CORE-04 approval workflow.</summary>
[Audited]
public class StaffLeaveRequest : CampusEntity, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid UserId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public bool HalfDay { get; set; }

    /// <summary>Working days counted (weekends and holidays excluded).</summary>
    public decimal Days { get; set; }
    public required string Reason { get; set; }
    public LeaveStatus Status { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public string? DecisionNote { get; set; }
}
