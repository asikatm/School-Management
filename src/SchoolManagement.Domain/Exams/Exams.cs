using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Exams;

public enum ExamType
{
    ClassTest = 0,
    MidTerm = 1,
    Final = 2,
    Mock = 3,
    BoardPractice = 4
}

public enum ResultStatus
{
    /// <summary>Marks are still being entered.</summary>
    NotReady = 0,

    /// <summary>Results are calculated and frozen into report cards, but not visible to families.</summary>
    Finalised = 1,

    /// <summary>Finalised and waiting for the embargo time.</summary>
    Scheduled = 2,
    Published = 3
}

/// <summary>EXM-01: an examination (class test, mid-term, final, mock, board practice) for some classes in a year.</summary>
[Audited]
public class Exam : SoftDeleteEntity, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid? TermId { get; set; }

    /// <summary>Null means every campus.</summary>
    public Guid? CampusId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public ExamType Type { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>Date sheet (and admit cards) visible in the portal and on the website.</summary>
    public bool SchedulePublished { get; set; }
    public DateTime? SchedulePublishedAtUtc { get; set; }
    public ResultStatus ResultStatus { get; set; }
    public DateTime? FinalisedAtUtc { get; set; }

    /// <summary>EXM-06 embargo: results become visible at this time.</summary>
    public DateTime? PublishAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public ICollection<ExamClass> Classes { get; set; } = [];
}

/// <summary>A class sitting the exam, with an optional grading scheme instead of its programme's default (EXM-04).</summary>
public class ExamClass : Entity
{
    public Guid ExamId { get; set; }
    public Guid YearGroupId { get; set; }
    public Guid? GradingSchemeId { get; set; }
}

/// <summary>EXM-01: one subject paper of the date sheet for a class.</summary>
[Audited]
public class ExamPaper : Entity
{
    public Guid ExamId { get; set; }
    public Guid YearGroupId { get; set; }
    public Guid SubjectId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public ICollection<ExamPaperComponent> Components { get; set; } = [];
}

/// <summary>EXM-03: a marked part of a paper (written, practical, oral, CA) with its maximum and weight in the subject %.</summary>
public class ExamPaperComponent : Entity
{
    public Guid ExamPaperId { get; set; }
    public required string Name { get; set; }
    public decimal MaxMarks { get; set; }
    public decimal WeightPercent { get; set; }
    public int Sequence { get; set; }

    /// <summary>Filled from the LRN-08 gradebook (moderated term %) instead of typed in.</summary>
    public bool FromGradebook { get; set; }
}

/// <summary>EXM-02: a student's room and seat for an exam.</summary>
public class ExamSeat : CampusRecord
{
    public Guid ExamId { get; set; }
    public Guid StudentId { get; set; }
    public Guid RoomId { get; set; }
    public int SeatNo { get; set; }
}

/// <summary>EXM-02: the invigilator of a room on an exam day.</summary>
public class ExamInvigilator : CampusRecord
{
    public Guid ExamId { get; set; }
    public DateOnly Date { get; set; }
    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }
}

public enum MarkSheetStatus
{
    Draft = 0,
    Submitted = 1,

    /// <summary>The second (verification) entry matches the first.</summary>
    Verified = 2,

    /// <summary>Approved by the moderator and locked.</summary>
    Approved = 3
}

/// <summary>EXM-03: the mark sheet of one paper for one section: entry, verification, moderation and lock.</summary>
[Audited]
public class MarkSheet : CampusRecord
{
    public Guid ExamPaperId { get; set; }
    public Guid SectionId { get; set; }
    public MarkSheetStatus Status { get; set; }
    public Guid? EnteredByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int ReopenCount { get; set; }
    public string? ReopenReason { get; set; }
}

public enum MarkCode
{
    None = 0,
    Absent = 1,
    Exempt = 2
}

/// <summary>EXM-03: a student's marks in one component, with the independent second entry used for verification.</summary>
[Audited]
public class ExamMark : CampusRecord
{
    public Guid ExamPaperId { get; set; }
    public Guid ComponentId { get; set; }
    public Guid StudentId { get; set; }
    public decimal? Marks { get; set; }
    public MarkCode Code { get; set; }
    public decimal? SecondMarks { get; set; }
    public MarkCode? SecondCode { get; set; }
}

public enum Conduct
{
    Excellent = 0,
    Good = 1,
    Satisfactory = 2,
    NeedsImprovement = 3
}

/// <summary>EXM-05: the class teacher's and principal's remarks, conduct and co-curricular notes for a student's report card.</summary>
[Audited]
public class ReportCardRemark : CampusRecord
{
    public Guid ExamId { get; set; }
    public Guid StudentId { get; set; }
    public string? TeacherRemark { get; set; }
    public string? PrincipalRemark { get; set; }
    public Conduct? Conduct { get; set; }
    public string? CoCurricular { get; set; }
}

/// <summary>
/// EXM-05/06: a student's frozen result for an exam — the report card. Subject lines are a JSON snapshot, signed with
/// an HMAC so the QR verification page can prove the printed card was not altered.
/// </summary>
[Audited]
public class ExamResult : CampusRecord, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid ExamId { get; set; }
    public Guid StudentId { get; set; }
    public Guid SectionId { get; set; }
    public Guid YearGroupId { get; set; }
    public decimal Percent { get; set; }
    public string? Grade { get; set; }
    public decimal? Gpa { get; set; }
    public bool Passed { get; set; }
    public int FailedSubjects { get; set; }
    public int? SectionRank { get; set; }
    public int? ClassRank { get; set; }
    public decimal? Percentile { get; set; }
    public decimal? AttendancePercent { get; set; }
    public required string SubjectsJson { get; set; }
    public string? TeacherRemark { get; set; }
    public string? PrincipalRemark { get; set; }
    public Conduct? Conduct { get; set; }
    public string? CoCurricular { get; set; }
    public required string VerificationCode { get; set; }
    public required string Signature { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}
