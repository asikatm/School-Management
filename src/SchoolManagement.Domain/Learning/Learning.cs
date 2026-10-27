using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Learning;

/// <summary>LRN-02: homework or an assignment set for one section in one subject.</summary>
[Audited]
public class Assignment : CampusEntity, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid SectionId { get; set; }
    public Guid SubjectId { get; set; }
    public required string Title { get; set; }
    public string? Instructions { get; set; }
    public DateOnly SetOn { get; set; }

    /// <summary>Due at this local (Asia/Dhaka) date and time.</summary>
    public DateTime DueAtLocal { get; set; }

    /// <summary>Null when the work is not marked (e.g. reading).</summary>
    public decimal? MaxMarks { get; set; }

    /// <summary>Students hand the work in online (files/photos); otherwise it is checked in class.</summary>
    public bool OnlineSubmission { get; set; } = true;
    public bool AllowLate { get; set; } = true;

    /// <summary>Deducted from the marks of a late submission, as a % of the marks awarded.</summary>
    public decimal LatePenaltyPercent { get; set; }
    public string? AttachmentPath { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentType { get; set; }
    public Guid? PostedByUserId { get; set; }

    /// <summary>When the "due tomorrow" reminder went to students and guardians.</summary>
    public DateTime? ReminderSentAtUtc { get; set; }
}

public enum SubmissionStatus
{
    Submitted = 0,
    Graded = 1,

    /// <summary>The teacher asked for the work to be redone; the student may submit again.</summary>
    ReturnedForRedo = 2
}

/// <summary>LRN-02: a student's hand-in (one per student per assignment, replaced on resubmission) and the teacher's mark.</summary>
[Audited]
public class AssignmentSubmission : CampusRecord
{
    public Guid AssignmentId { get; set; }
    public Guid StudentId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public string? Text { get; set; }
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public string? FileType { get; set; }
    public bool IsLate { get; set; }
    public SubmissionStatus Status { get; set; }

    /// <summary>Marks the teacher gave, before any late penalty.</summary>
    public decimal? Marks { get; set; }

    /// <summary>Marks after the late penalty; this is what the gradebook uses.</summary>
    public decimal? FinalMarks { get; set; }
    public string? Feedback { get; set; }
    public Guid? GradedByUserId { get; set; }
    public DateTime? GradedAtUtc { get; set; }
}

/// <summary>LRN-03: one line of a section's daily class diary (topics, homework, reminders, uniform/kit notes).</summary>
[Audited]
public class ClassDiaryEntry : CampusEntity, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid SectionId { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>Null for a general note (e.g. from the class teacher).</summary>
    public Guid? SubjectId { get; set; }
    public string? Topics { get; set; }
    public string? Homework { get; set; }
    public string? Reminders { get; set; }
    public string? KitNotes { get; set; }
    public Guid? PostedByUserId { get; set; }
}

public enum ResourceType
{
    Notes = 0,
    Slides = 1,
    Video = 2,
    PastPaper = 3,
    Worksheet = 4,
    Link = 5,
    Other = 6
}

public enum ResourceAudience
{
    /// <summary>Teachers only (e.g. mark schemes).</summary>
    Staff = 0,

    /// <summary>Teachers, students and their guardians.</summary>
    StudentsAndParents = 1
}

/// <summary>LRN-04: notes, slides, videos and past papers for a subject and class, as an uploaded file or a link.</summary>
[Audited]
public class LearningResource : SoftDeleteEntity
{
    /// <summary>Null means every campus.</summary>
    public Guid? CampusId { get; set; }
    public Guid SubjectId { get; set; }

    /// <summary>Null means every class; a section narrows it further.</summary>
    public Guid? YearGroupId { get; set; }
    public Guid? SectionId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Topic { get; set; }
    public ResourceType Type { get; set; }
    public ResourceAudience Audience { get; set; } = ResourceAudience.StudentsAndParents;
    public string? Url { get; set; }
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public string? FileType { get; set; }
    public long? FileSize { get; set; }
    public int Downloads { get; set; }
}

/// <summary>LRN-04: download tracking — who opened a resource and when.</summary>
public class ResourceDownload : Entity
{
    public Guid ResourceId { get; set; }
    public Guid? UserId { get; set; }
    public DateTime AtUtc { get; set; }
}

/// <summary>LRN-08: a continuous-assessment category (classwork, projects, practicals, quizzes…) and its weight in the term grade.</summary>
public class GradebookCategory : SoftDeleteEntity
{
    /// <summary>Null is the school default; a class may set its own weights.</summary>
    public Guid? YearGroupId { get; set; }
    public required string Name { get; set; }
    public decimal WeightPercent { get; set; }
    public int Sequence { get; set; }
}

/// <summary>LRN-08: one assessed piece of work in a section's subject gradebook for a term.</summary>
[Audited]
public class GradebookItem : CampusEntity, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid SectionId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid TermId { get; set; }
    public Guid CategoryId { get; set; }
    public required string Title { get; set; }
    public DateOnly Date { get; set; }
    public decimal MaxMarks { get; set; }

    /// <summary>Marks are taken from this homework's graded submissions.</summary>
    public Guid? AssignmentId { get; set; }
}

/// <summary>LRN-08: a student's mark for a gradebook item. Excused work is left out of the average.</summary>
[Audited]
public class GradebookMark : CampusRecord
{
    public Guid ItemId { get; set; }
    public Guid StudentId { get; set; }
    public decimal? Marks { get; set; }
    public bool Excused { get; set; }
    public string? Comment { get; set; }
}

public enum ModerationStatus
{
    Open = 0,
    Submitted = 1,
    Approved = 2,
    Returned = 3
}

/// <summary>
/// LRN-08: moderation of a section's term grades in one subject by the head of department. Approval locks the
/// gradebook; the adjustment (± percentage points) is applied to every student's term %.
/// </summary>
[Audited]
public class GradebookModeration : CampusRecord, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid SectionId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid TermId { get; set; }
    public ModerationStatus Status { get; set; }
    public decimal Adjustment { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? ModeratedByUserId { get; set; }
    public DateTime? ModeratedAtUtc { get; set; }
    public string? Note { get; set; }
}
