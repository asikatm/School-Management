using SchoolManagement.Domain.Common;
using SchoolManagement.Domain.Core;

namespace SchoolManagement.Domain.Academics;

public enum RoomType
{
    Classroom = 0,
    ScienceLab = 1,
    ComputerLab = 2,
    Library = 3,
    Hall = 4,
    Playground = 5,
    Other = 6
}

/// <summary>Rooms used for timetable clash checks (ACA-04). Booking of rooms and equipment (ACA-06) builds on this later.</summary>
[Audited]
public class Room : CampusEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public RoomType Type { get; set; }
    public int Capacity { get; set; } = 30;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

/// <summary>ACA-02: a set of optional/elective subjects from which a student picks between Min and Max (e.g. IAL options).</summary>
public class SubjectGroup : SoftDeleteEntity
{
    public Guid YearGroupId { get; set; }
    public required string Name { get; set; }
    public int MinChoices { get; set; }
    public int MaxChoices { get; set; } = 1;
    public int Sequence { get; set; }
}

/// <summary>ACA-01: a subject taught in a year-group, with its type and weekly periods.</summary>
[Audited]
public class CurriculumSubject : Entity
{
    public Guid YearGroupId { get; set; }
    public Guid SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public SubjectType Type { get; set; }
    public Guid? SubjectGroupId { get; set; }
    public int PeriodsPerWeek { get; set; } = 5;
    public int Sequence { get; set; }

    /// <summary>When set, the auto-timetable places this subject in a room of this type (e.g. science practicals in a lab).</summary>
    public RoomType? PreferredRoomType { get; set; }
}

/// <summary>ACA-02: an optional/elective subject a student takes in a year. Compulsory subjects are implied by the curriculum.</summary>
public class StudentSubject : CampusRecord, IAcademicYearScoped
{
    public Guid StudentId { get; set; }
    public Guid AcademicYearId { get; set; }
    public Guid SubjectId { get; set; }
}

/// <summary>ACA-03: who teaches which subject to which section, and for how many periods a week.</summary>
[Audited]
public class TeacherAllocation : CampusRecord, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid SectionId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid TeacherUserId { get; set; }
    public int PeriodsPerWeek { get; set; }
}

/// <summary>ACA-04: the bell schedule of a campus (optionally per shift). Breaks are kept so routines print correctly.</summary>
public class PeriodDefinition : CampusRecord
{
    public Guid? ShiftId { get; set; }
    public int PeriodNo { get; set; }
    public required string Name { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsBreak { get; set; }
}

/// <summary>ACA-04: one cell of a section's weekly timetable.</summary>
[Audited]
public class TimetableSlot : CampusRecord, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid SectionId { get; set; }
    public DayOfWeek Day { get; set; }
    public int PeriodNo { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? TeacherUserId { get; set; }
    public Guid? RoomId { get; set; }
    public string? Note { get; set; }
}

public enum SubstitutionStatus
{
    Assigned = 0,
    Cancelled = 1
}

/// <summary>ACA-05: cover for one timetable period on one date. Section/period/subject are copied so reports survive timetable edits.</summary>
[Audited]
public class Substitution : CampusEntity
{
    public DateOnly Date { get; set; }
    public Guid TimetableSlotId { get; set; }
    public Guid SectionId { get; set; }
    public int PeriodNo { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? AbsentTeacherUserId { get; set; }
    public Guid SubstituteUserId { get; set; }
    public string? Reason { get; set; }
    public SubstitutionStatus Status { get; set; } = SubstitutionStatus.Assigned;
}

public enum CalendarEventType
{
    Event = 0,
    ExamWindow = 1,
    ParentTeacherMeeting = 2,
    Activity = 3,
    Meeting = 4,
    Other = 5
}

/// <summary>ACA-07: school calendar entries besides terms and holidays (exam windows, PTMs, events).</summary>
[Audited]
public class CalendarEvent : SoftDeleteEntity, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }

    /// <summary>Null means every campus.</summary>
    public Guid? CampusId { get; set; }
    public required string Title { get; set; }
    public string? TitleBn { get; set; }
    public CalendarEventType Type { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public bool ForStaff { get; set; } = true;
    public bool ForParents { get; set; } = true;

    /// <summary>Shown on the public website and in the public iCal feed (WEB-01).</summary>
    public bool IsPublic { get; set; }
    public bool IsPublished { get; set; }
}
