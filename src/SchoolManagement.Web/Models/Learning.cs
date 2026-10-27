using SchoolManagement.Domain.Learning;

namespace SchoolManagement.Web.Models;

// Mirrors SchoolManagement.Api/Modules/Learning (LRN: homework, class diary, resources, gradebook) and the family portal.

// ---- Homework (LRN-02)
public record AssignmentView(Guid Id, Guid CampusId, Guid SectionId, string SectionLabel, Guid SubjectId, string SubjectName, string Title, string? Instructions,
    DateOnly SetOn, DateTime DueAtLocal, decimal? MaxMarks, bool OnlineSubmission, bool AllowLate, decimal LatePenaltyPercent, bool HasAttachment,
    string? AttachmentName, string? PostedBy, int Students, int Submitted, int Graded, bool InGradebook, bool CanEdit);
public record SubmissionRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, Guid? SubmissionId, DateTime? SubmittedAtUtc, bool IsLate, string? Text,
    bool HasFile, string? FileName, SubmissionStatus? Status, decimal? Marks, decimal? FinalMarks, string? Feedback);
public record AssignmentDetail(AssignmentView Assignment, IReadOnlyList<SubmissionRow> Students);
public record GradeEntry(Guid StudentId, decimal? Marks, string? Feedback, bool ReturnForRedo);
public record GradeRequest(List<GradeEntry> Entries);
public record StudentHomework(Guid AssignmentId, string SubjectName, string Title, string? Instructions, DateOnly SetOn, DateTime DueAtLocal, decimal? MaxMarks,
    bool OnlineSubmission, bool AllowLate, decimal LatePenaltyPercent, bool HasAttachment, string? AttachmentName, bool Overdue, SubmissionStatus? Status,
    DateTime? SubmittedAtUtc, bool IsLate, string? SubmittedText, string? SubmittedFileName, decimal? FinalMarks, string? Feedback, bool CanSubmit);

// ---- Class diary (LRN-03)
public record DiaryEntryRequest(Guid SectionId, DateOnly Date, Guid? SubjectId, string? Topics, string? Homework, string? Reminders, string? KitNotes);
public record DiaryEntryView(Guid Id, Guid SectionId, DateOnly Date, Guid? SubjectId, string? SubjectName, string? Topics, string? Homework, string? Reminders,
    string? KitNotes, string? PostedBy, DateTime PostedAtUtc, bool CanEdit);
public record DigestHomework(Guid AssignmentId, string SubjectName, string Title, DateTime DueAtLocal);
public record DailyDigest(Guid SectionId, string SectionLabel, DateOnly Date, string? ClosedReason, IReadOnlyList<DiaryEntryView> Entries,
    IReadOnlyList<DigestHomework> HomeworkSet, IReadOnlyList<DigestHomework> DueNext, bool CanWrite);

// ---- Resources (LRN-04)
public record ResourceView(Guid Id, Guid? CampusId, Guid SubjectId, string SubjectName, Guid? YearGroupId, string? ClassName, Guid? SectionId, string? SectionLabel,
    string Title, string? Description, string? Topic, ResourceType Type, ResourceAudience Audience, string? Url, bool HasFile, string? FileName, long? FileSize,
    int Downloads, string? SharedBy, DateTime SharedAtUtc, bool CanEdit);
public record ResourceDownloadView(string? User, DateTime AtUtc);
public record ResourceLink(string Url);

// ---- Gradebook (LRN-08)
public record CategoryView(Guid Id, Guid? YearGroupId, string Name, decimal WeightPercent, int Sequence);
public record CategoryRequest(string Name, decimal WeightPercent);
public record CategoriesRequest(Guid? YearGroupId, List<CategoryRequest> Categories);
public record GradebookItemRequest(Guid SectionId, Guid SubjectId, Guid TermId, Guid CategoryId, string Title, DateOnly Date, decimal MaxMarks);
public record GradebookItemView(Guid Id, Guid CategoryId, string CategoryName, string Title, DateOnly Date, decimal MaxMarks, Guid? AssignmentId);
public record MarkCell(decimal? Marks, bool Excused, string? Comment);
public record GradebookStudentRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, Dictionary<Guid, MarkCell> Marks,
    Dictionary<Guid, decimal> CategoryPercents, decimal? TermPercent, decimal? ModeratedPercent, string? Grade);
public record ModerationView(ModerationStatus Status, decimal Adjustment, string? SubmittedBy, DateTime? SubmittedAtUtc, string? ModeratedBy, DateTime? ModeratedAtUtc, string? Note);
public record GradebookView(Guid SectionId, string SectionLabel, Guid SubjectId, string SubjectName, Guid TermId, string TermName, IReadOnlyList<CategoryView> Categories,
    IReadOnlyList<GradebookItemView> Items, IReadOnlyList<GradebookStudentRow> Students, ModerationView Moderation, decimal? ClassAverage, bool CanEdit, bool CanModerate);
public record MarkEntryRequest(Guid ItemId, Guid StudentId, decimal? Marks, bool Excused, string? Comment);
public record SaveGradebookMarksRequest(List<MarkEntryRequest> Entries);
public record ModerationDecision(bool Approve, decimal Adjustment, string? Note);
public record ModerationQueueRow(Guid SectionId, Guid CampusId, string SectionLabel, Guid SubjectId, string SubjectName, Guid TermId, string TermName, ModerationStatus Status,
    string? SubmittedBy, DateTime? SubmittedAtUtc, int Items, decimal? ClassAverage);

// ---- Family portal
public record PortalStudent(Guid StudentId, string AdmissionNo, string Name, string? ClassName, Guid? SectionId, Guid CampusId, bool IsSelf);
public record StudentPortalAccount(string UserName, string? TemporaryPassword, bool Created);
