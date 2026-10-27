using SchoolManagement.Domain.Exams;

namespace SchoolManagement.Web.Models;

// Mirrors SchoolManagement.Api/Modules/Exams (EXM: schedule, seating, marks, results, report cards, transcripts, analysis).

// ---- Exams & date sheet (EXM-01)
public record ExamClassRequest(Guid YearGroupId, Guid? GradingSchemeId);
public record ExamRequest(Guid AcademicYearId, Guid? TermId, Guid? CampusId, string Code, string Name, string? NameBn, ExamType Type, DateOnly StartDate,
    DateOnly EndDate, List<ExamClassRequest> Classes);
public record ExamClassView(Guid YearGroupId, string ClassName, Guid? GradingSchemeId, string? GradingScheme);
public record ExamView(Guid Id, Guid AcademicYearId, string AcademicYear, Guid? TermId, string? TermName, Guid? CampusId, string? CampusName, string Code, string Name,
    string? NameBn, ExamType Type, DateOnly StartDate, DateOnly EndDate, bool SchedulePublished, ResultStatus ResultStatus, DateTime? PublishAtUtc,
    DateTime? PublishedAtUtc, IReadOnlyList<ExamClassView> Classes, int Papers, int MarkSheets, int MarkSheetsApproved);
public record ComponentRequest(string Name, decimal MaxMarks, decimal WeightPercent, bool FromGradebook);
public record PaperRequest(Guid YearGroupId, Guid SubjectId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, List<ComponentRequest> Components);
public record ComponentView(Guid Id, string Name, decimal MaxMarks, decimal WeightPercent, int Sequence, bool FromGradebook);
public record PaperView(Guid Id, Guid YearGroupId, string ClassName, Guid SubjectId, string SubjectCode, string SubjectName, DateOnly Date, TimeOnly StartTime,
    TimeOnly EndTime, IReadOnlyList<ComponentView> Components, int Candidates);
public record ExamClash(DateOnly Date, string ClassName, string FirstSubject, string FirstTime, string SecondSubject, string SecondTime, int Students,
    IReadOnlyList<string> Examples);
public record DateSheetPaper(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string SubjectCode, string SubjectName);
public record PortalExam(Guid ExamId, string Name, string? NameBn, ExamType Type, DateOnly StartDate, DateOnly EndDate, string? Room, int? SeatNo,
    IReadOnlyList<DateSheetPaper> Papers);

// ---- Seating (EXM-02)
public record SeatingRequest(Guid CampusId, List<Guid> RoomIds);
public record SeatRow(int SeatNo, Guid StudentId, string AdmissionNo, string Name, string ClassLabel);
public record InvigilatorView(DateOnly Date, Guid UserId, string Name);
public record RoomSeating(Guid RoomId, string RoomName, int Capacity, IReadOnlyList<SeatRow> Seats, IReadOnlyList<InvigilatorView> Invigilators);
public record SeatingView(Guid ExamId, Guid CampusId, int Candidates, int Seated, IReadOnlyList<DateOnly> Days, IReadOnlyList<RoomSeating> Rooms);
public record InvigilatorRequest(Guid CampusId, DateOnly Date, Guid RoomId, List<Guid> UserIds);
public record SeatLookup(Guid StudentId, string AdmissionNo, string Name, string ClassLabel, string? Room, int? SeatNo);

// ---- Marks (EXM-03)
public record MarkSheetSummary(Guid PaperId, Guid SectionId, string SectionLabel, Guid CampusId, string ClassName, string SubjectName, DateOnly Date, MarkSheetStatus Status,
    int Candidates, int Complete, string? EnteredBy, string? VerifiedBy, string? ApprovedBy, bool CanEnter);
public record MyMarkSheet(Guid ExamId, string ExamName, MarkSheetSummary Sheet);
public record MarkCellView(Guid ComponentId, decimal? Marks, MarkCode Code, decimal? SecondMarks, MarkCode? SecondCode, bool Mismatch);
public record MarkRowView(Guid StudentId, string AdmissionNo, string Name, int? RollNo, IReadOnlyList<MarkCellView> Cells, decimal? Percent, string? Grade, string? Status);
public record MarkSheetView(Guid PaperId, Guid SectionId, string SectionLabel, string ExamName, string ClassName, string SubjectName, DateOnly Date,
    IReadOnlyList<ComponentView> Components, MarkSheetStatus Status, bool DoubleEntry, string? EnteredBy, DateTime? SubmittedAtUtc, string? VerifiedBy,
    string? ApprovedBy, DateTime? ApprovedAtUtc, int ReopenCount, string? ReopenReason, int Mismatches, bool CanEnter, bool CanVerify, bool CanApprove,
    bool CanReopen, IReadOnlyList<MarkRowView> Students);
public record MarkInput(Guid StudentId, Guid ComponentId, decimal? Marks, MarkCode Code);
public record SaveMarksRequest(List<MarkInput> Entries, bool SecondEntry = false);
public record MarkImportRow(int Row, string AdmissionNo, string? Name, IReadOnlyList<string> Errors);
public record MarkImportResult(bool DryRun, int Rows, int Valid, bool Saved, IReadOnlyList<MarkImportRow> Problems);
public record ReasonRequest(string Reason);

// ---- Results (EXM-04/05/06)
public record ComponentResult(string Name, decimal? Marks, decimal MaxMarks, string? Code);
public record SubjectResult(Guid SubjectId, string Code, string Name, IReadOnlyList<ComponentResult> Components, decimal? Percent, string? Grade, string? Descriptor,
    string? DescriptorBn, decimal? GradePoint, bool Passed, string? Status, decimal? HighestInSection);
public record ResultRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, Guid SectionId, string SectionLabel, Guid CampusId, Guid YearGroupId, decimal Percent,
    string? Grade, decimal? Gpa, bool Passed, int FailedSubjects, int? SectionRank, int? ClassRank, decimal? Percentile, decimal? AttendancePercent,
    IReadOnlyList<SubjectResult> Subjects, bool Complete);
public record SubjectHead(Guid SubjectId, string Code, string Name);
public record TabulationView(Guid ExamId, string ExamName, ResultStatus Status, bool Preview, IReadOnlyList<string> PendingSheets, IReadOnlyList<SubjectHead> Subjects,
    IReadOnlyList<ResultRow> Students);
public record RemarkInput(Guid StudentId, string? TeacherRemark, string? PrincipalRemark, Conduct? Conduct, string? CoCurricular);
public record SaveRemarksRequest(List<RemarkInput> Entries);
public record RemarkRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, string? TeacherRemark, string? PrincipalRemark, Conduct? Conduct, string? CoCurricular);
public record RemarksView(Guid ExamId, Guid SectionId, string SectionLabel, bool CanEditTeacher, bool CanEditPrincipal, IReadOnlyList<RemarkRow> Students);
public record PublishRequest(DateTime? PublishAtUtc);
public record StudentResultSummary(Guid ExamId, string ExamName, ExamType Type, string AcademicYear, DateOnly EndDate, decimal Percent, string? Grade, decimal? Gpa,
    bool Passed, int? SectionRank, DateTime? PublishedAtUtc);
public record VerificationView(bool Found, bool Intact, string Message, string? School, string? StudentName, string? AdmissionNo, string? ClassName, string? ExamName,
    string? AcademicYear, decimal? Percent, string? Grade, decimal? Gpa, bool? Passed, DateTime? IssuedAtUtc);
public record TranscriptExam(Guid ExamId, string ExamName, ExamType Type, decimal Percent, string? Grade, decimal? Gpa, bool Passed, int? ClassRank);
public record TranscriptYear(Guid AcademicYearId, string AcademicYear, string ClassName, IReadOnlyList<TranscriptExam> Exams);
public record TranscriptMark(string AcademicYear, string ExamName, decimal? Percent, string? Grade, string? Status);
public record TranscriptSubject(string SubjectName, IReadOnlyList<TranscriptMark> Marks);
public record Transcript(Guid StudentId, string AdmissionNo, string Name, string? NameBn, DateOnly DateOfBirth, string Campus, DateOnly AdmissionDate,
    IReadOnlyList<TranscriptYear> Years, IReadOnlyList<TranscriptSubject> Subjects, decimal? CumulativeGpa, decimal? CumulativePercent);

// ---- Analysis (EXM-10)
public record GradeCount(string Grade, int Count);
public record SubjectAnalysis(Guid SubjectId, string Name, int Candidates, decimal Average, decimal PassPercent, decimal Highest, decimal Lowest,
    IReadOnlyList<GradeCount> Grades, decimal? CompareAverage);
public record GroupAnalysis(string Name, int Candidates, decimal Average, decimal PassPercent);
public record HeatCell(string Section, string Subject, decimal Average);
public record TopStudent(string Name, string AdmissionNo, string ClassLabel, decimal Percent, string? Grade, decimal? Gpa);
public record AtRiskStudent(string Name, string AdmissionNo, string ClassLabel, decimal Percent, IReadOnlyList<string> FailedSubjects);
public record ResultAnalysis(Guid ExamId, string ExamName, bool Preview, int Candidates, int Passed, decimal PassPercent, decimal Average, IReadOnlyList<GradeCount> OverallGrades,
    IReadOnlyList<SubjectAnalysis> Subjects, IReadOnlyList<GroupAnalysis> Sections, IReadOnlyList<GroupAnalysis> Teachers, IReadOnlyList<GroupAnalysis> Campuses,
    IReadOnlyList<HeatCell> Heatmap, IReadOnlyList<TopStudent> TopPerformers, IReadOnlyList<AtRiskStudent> AtRisk, string? CompareExamName, decimal? CompareAverage,
    decimal? ComparePassPercent);
