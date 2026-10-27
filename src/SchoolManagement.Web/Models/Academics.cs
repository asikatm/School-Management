using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Core;

namespace SchoolManagement.Web.Models;

// Mirrors SchoolManagement.Api/Modules/Academics (ACA: curriculum, allocation, timetable, substitution, calendar).

// ---- Set-up
public record RoomView(Guid Id, Guid CampusId, string Code, string Name, RoomType Type, int Capacity, bool IsActive, string? Notes);
public record RoomRequest(Guid CampusId, string Code, string Name, RoomType Type, int Capacity, bool IsActive, string? Notes);
public record PeriodView(int PeriodNo, string Name, TimeOnly StartTime, TimeOnly EndTime, bool IsBreak);
public record PeriodRequest(int PeriodNo, string Name, TimeOnly StartTime, TimeOnly EndTime, bool IsBreak);
public record BellScheduleRequest(Guid CampusId, Guid? ShiftId, List<PeriodRequest> Periods);
public record TeacherOption(Guid Id, string FullName, string UserName);

// ---- Curriculum & subject choices
public record SubjectGroupView(Guid Id, string Name, int MinChoices, int MaxChoices);
public record CurriculumRow(Guid SubjectId, string Code, string Name, SubjectType Type, string? GroupName, int PeriodsPerWeek, RoomType? PreferredRoomType);
public record CurriculumView(Guid YearGroupId, IReadOnlyList<SubjectGroupView> Groups, IReadOnlyList<CurriculumRow> Subjects, int TotalPeriods);
public record GroupRequest(string Name, int MinChoices, int MaxChoices);
public record CurriculumRowRequest(Guid SubjectId, SubjectType Type, string? GroupName, int PeriodsPerWeek, RoomType? PreferredRoomType);
public record CurriculumRequest(List<GroupRequest> Groups, List<CurriculumRowRequest> Subjects);
public record StudentChoiceRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, IReadOnlyList<Guid> SubjectIds, IReadOnlyList<string> Problems);
public record SubjectChoicesView(Guid SectionId, string SectionLabel, Guid AcademicYearId, IReadOnlyList<SubjectGroupView> Groups,
    IReadOnlyList<CurriculumRow> Choosable, IReadOnlyList<StudentChoiceRow> Students);
public record SubjectChoiceRequest(Guid AcademicYearId, List<Guid> SubjectIds);

// ---- Allocation
public record AllocationView(Guid Id, Guid SectionId, string SectionLabel, Guid SubjectId, string SubjectCode, string SubjectName, Guid TeacherUserId,
    string TeacherName, int PeriodsPerWeek, int Placed);
public record AllocationRowRequest(Guid SubjectId, Guid TeacherUserId, int PeriodsPerWeek);
public enum LoadStatus { Ok = 0, Overloaded = 1, UnderUsed = 2 }
public record TeacherLoad(Guid TeacherUserId, string Name, int AllocatedPeriods, int PlacedPeriods, int Sections, IReadOnlyList<string> Subjects, LoadStatus Status);
public record AllocationGap(Guid SectionId, string SectionLabel, Guid SubjectId, string SubjectName, SubjectType Type, int PeriodsPerWeek);

// ---- Timetable
public record TimetableIssue(string Severity, string Message);
public record SectionInfo(Guid Id, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, Guid? ShiftId, string Label, string? HomeRoom, Guid? ClassTeacherUserId);
public record SlotView(Guid Id, Guid SectionId, string SectionLabel, DayOfWeek Day, int PeriodNo, string PeriodName, TimeOnly StartTime, TimeOnly EndTime,
    Guid SubjectId, string SubjectCode, string SubjectName, Guid? TeacherUserId, string? TeacherName, Guid? RoomId, string? RoomName, string? Note,
    bool IsCover = false, string? CoverNote = null);
public record PlanTeacher(Guid UserId, string Name, int Periods);
public record SubjectPlan(Guid SubjectId, string Code, string Name, SubjectType Type, Guid? GroupId, string? GroupName, int Required, int Placed,
    IReadOnlyList<PlanTeacher> Teachers, RoomType? PreferredRoomType);
public record SectionTimetable(SectionInfo Section, IReadOnlyList<DayOfWeek> Days, IReadOnlyList<PeriodView> Periods, IReadOnlyList<SlotView> Slots,
    IReadOnlyList<SubjectPlan> Plan);
public record SlotRequest(Guid SectionId, DayOfWeek Day, int PeriodNo, Guid SubjectId, Guid? TeacherUserId, Guid? RoomId, string? Note, bool Parallel = false);
public record SlotSaveResult(IReadOnlyList<SlotView> Cell, IReadOnlyList<TimetableIssue> Warnings);
public record AutoTimetableRequest(bool ClearExisting = true);
public record AutoTimetableResult(int Placed, IReadOnlyList<string> Unplaced, IReadOnlyList<TimetableIssue> Warnings);
public record PersonTimetable(Guid Id, string Name, IReadOnlyList<DayOfWeek> Days, IReadOnlyList<SlotView> Slots, int PeriodsPerWeek);
public record DayItem(SlotView Slot, string Status, string? Detail);
public record ClassTeacherSection(SectionInfo Section, int Students, IReadOnlyList<SlotView> Today, IReadOnlyList<SubjectPlan> Gaps);
public record MyDay(DateOnly Date, DayOfWeek Day, string? Holiday, IReadOnlyList<DayItem> Items, IReadOnlyList<ClassTeacherSection> ClassSections);

// ---- Substitution
public record CoverCandidate(Guid UserId, string Name, bool TeachesSubject, int CoversThisWeek, int PeriodsThatDay);
public record CoverNeed(SlotView Slot, Guid? SubstitutionId, Guid? SubstituteUserId, string? SubstituteName, string? Reason, IReadOnlyList<CoverCandidate> Candidates);
public record CoverNeeds(DateOnly Date, DayOfWeek Day, Guid TeacherUserId, string TeacherName, string? Holiday, IReadOnlyList<CoverNeed> Periods);
public record AssignCoverRequest(Guid TimetableSlotId, DateOnly Date, Guid SubstituteUserId, string? Reason);
public record SubstitutionView(Guid Id, DateOnly Date, string PeriodName, TimeOnly StartTime, TimeOnly EndTime, string SectionLabel, string SubjectName,
    Guid? AbsentTeacherUserId, string? AbsentTeacherName, Guid SubstituteUserId, string SubstituteName, string? Reason, SubstitutionStatus Status);
public record CoverReportRow(Guid UserId, string Name, int CoversGiven, int PeriodsCoveredForThem);

// ---- Calendar
public enum CalendarItemKind { Term = 0, Holiday = 1, Event = 2 }
public record CalendarItem(Guid Id, CalendarItemKind Kind, string Title, string? TitleBn, string Category, DateOnly StartDate, DateOnly EndDate,
    TimeOnly? StartTime, TimeOnly? EndTime, Guid? CampusId, string? Location, string? Description, bool IsPublic, bool IsPublished, bool ForStaff, bool ForParents);
public record CalendarEventRequest(Guid AcademicYearId, Guid? CampusId, string Title, string? TitleBn, CalendarEventType Type, DateOnly StartDate, DateOnly EndDate,
    TimeOnly? StartTime, TimeOnly? EndTime, string? Location, string? Description, bool ForStaff, bool ForParents, bool IsPublic, bool IsPublished);
