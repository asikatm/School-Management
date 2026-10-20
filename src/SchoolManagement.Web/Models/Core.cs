using SchoolManagement.Domain.Core;

namespace SchoolManagement.Web.Models;

// Mirrors SchoolManagement.Api/Modules/Core: CampusesController, AcademicYearsController (+ RolloverService),
// AcademicStructureController, MasterDataController and GradingSchemesController.

// ---- Campuses
public record CampusView(Guid Id, string Code, string Name, string? NameBn, string? Address, string? Phone, string? Email, string? HeadName, bool IsActive);
public record CampusRequest(string Code, string Name, string? NameBn, string? Address, string? Phone, string? Email, string? HeadName, bool IsActive = true);

// ---- Academic years & terms
public record TermView(Guid Id, string Name, string? NameBn, int Sequence, DateOnly StartDate, DateOnly EndDate, bool IsLocked);
public record AcademicYearView(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, AcademicYearStatus Status, bool IsCurrent, IReadOnlyList<TermView> Terms);
public record TermRequest(string Name, string? NameBn, int Sequence, DateOnly StartDate, DateOnly EndDate);
public record AcademicYearRequest(string Name, DateOnly StartDate, DateOnly EndDate, List<TermRequest>? Terms);

// ---- Roll-over wizard
public record RolloverRequest(string NewYearName, DateOnly StartDate, DateOnly EndDate, bool CopySections = true, bool CopySeatCapacities = true,
    bool PromoteStudents = true, bool LockSourceYear = false, bool MakeNewYearCurrent = false);
public record RolloverPreview(string SourceYear, int SectionsToCopy, int ActiveStudents, int ToPromote, int ToGraduate, IReadOnlyList<string> Warnings);
/// <summary>Mirrors the API's PromotionApplyResult as embedded in a roll-over result.</summary>
public record RolloverPromotionSummary(int Promoted, int Conditional, int Repeated, int Graduated, int Held);
public record RolloverResult(Guid NewAcademicYearId, string NewYearName, int TermsCreated, int SectionsCreated, int SeatCapacitiesCopied,
    RolloverPromotionSummary? Promotion, bool SourceLocked, IReadOnlyList<string> Notes);

// ---- Academic structure
public record YearGroupView(Guid Id, Guid ProgrammeId, string Code, string Name, string? NameBn, int Sequence, Guid? NextYearGroupId, bool IsFinalYear,
    int? MinAgeMonths, int? MaxAgeMonths, int AgeCutOffMonth, int AgeCutOffDay);
public record ProgrammeView(Guid Id, string Code, string Name, string? NameBn, int Sequence, Guid? DefaultGradingSchemeId, IReadOnlyList<YearGroupView> YearGroups);
public record ProgrammeRequest(string Code, string Name, string? NameBn, int Sequence, Guid? DefaultGradingSchemeId);
public record YearGroupRequest(Guid ProgrammeId, string Code, string Name, string? NameBn, int Sequence, Guid? NextYearGroupId, bool IsFinalYear,
    int? MinAgeMonths, int? MaxAgeMonths, int AgeCutOffMonth = 7, int AgeCutOffDay = 1);
public record SectionView(Guid Id, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string YearGroupName, string Name, int Capacity,
    Guid? ShiftId, Guid? ClassTeacherUserId, string? Room, int StudentCount);
public record SectionRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string Name, int Capacity, Guid? ShiftId, Guid? ClassTeacherUserId, string? Room);
public record ShiftView(Guid Id, Guid CampusId, string Name, TimeOnly StartTime, TimeOnly EndTime);
public record ShiftRequest(Guid CampusId, string Name, TimeOnly StartTime, TimeOnly EndTime);

// ---- Master data
public record SubjectView(Guid Id, string Code, string Name, string? NameBn, SubjectType Type, string? ExternalCode);
public record SubjectRequest(string Code, string Name, string? NameBn, SubjectType Type, string? ExternalCode);
public record HouseView(Guid Id, string Name, string? NameBn, string? Colour);
public record HouseRequest(string Name, string? NameBn, string? Colour);
public record HolidayView(Guid Id, Guid AcademicYearId, Guid? CampusId, string Name, string? NameBn, HolidayType Type, DateOnly StartDate, DateOnly EndDate);
public record HolidayRequest(Guid AcademicYearId, Guid? CampusId, string Name, string? NameBn, HolidayType Type, DateOnly StartDate, DateOnly EndDate);
public record FeeHeadView(Guid Id, string Code, string Name, string? NameBn, FeeFrequency Frequency, bool IsRefundable);
public record FeeHeadRequest(string Code, string Name, string? NameBn, FeeFrequency Frequency, bool IsRefundable);

// ---- Grading schemes
public record GradeBandView(string Grade, string? Descriptor, string? DescriptorBn, decimal MinPercent, decimal MaxPercent, decimal? GradePoint, bool IsPass);
public record GradingSchemeView(Guid Id, string Code, string Name, GradingScaleType ScaleType, IReadOnlyList<GradeBandView> Bands);
public record GradeBandRequest(string Grade, string? Descriptor, string? DescriptorBn, decimal MinPercent, decimal MaxPercent, decimal? GradePoint, bool IsPass = true);
public record GradingSchemeRequest(string Code, string Name, GradingScaleType ScaleType, List<GradeBandRequest> Bands);
