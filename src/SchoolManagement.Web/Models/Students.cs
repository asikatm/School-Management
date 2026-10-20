using SchoolManagement.Domain.Students;

namespace SchoolManagement.Web.Models;

// Mirrors of the records in SchoolManagement.Api.Modules.Students (StudentsController, StudentOperationsController, Services).
// Type names are prefixed where the API name is generic; the JSON shape is what matters.

// ---- Lookups (subset of the Core views)

public record StudentCampusOption(Guid Id, string Code, string Name, bool IsActive);
public record StudentAcademicYearOption(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, bool IsCurrent);
public record StudentYearGroupOption(Guid Id, Guid ProgrammeId, string Code, string Name, int Sequence, Guid? NextYearGroupId, bool IsFinalYear);
public record StudentProgrammeOption(Guid Id, string Code, string Name, int Sequence, IReadOnlyList<StudentYearGroupOption> YearGroups);
public record StudentSectionOption(Guid Id, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string YearGroupName, string Name, int Capacity, int StudentCount);

// ---- SIS-01..04

public record StudentListItem(Guid Id, string AdmissionNo, string Name, string? NameBn, string? Gender, DateOnly DateOfBirth, Guid CampusId, Guid? YearGroupId,
    string? ClassName, Guid? SectionId, string? Section, int? RollNo, StudentStatus Status, string? GuardianPhone);

public record StudentGuardianView(Guid GuardianId, string Name, string? NameBn, string Relationship, string Phone, string? Email, string? Occupation,
    bool IsPrimaryContact, bool IsAuthorisedPickup, bool IsEmergencyContact, bool CanViewFees, bool HasPortalAccount);

public record StudentEnrolmentView(Guid Id, Guid AcademicYearId, string AcademicYear, Guid CampusId, Guid YearGroupId, string ClassName, Guid? SectionId, string? Section,
    int? RollNo, DateOnly StartDate, DateOnly? EndDate, EnrolmentStatus Status, string? Note);

public record StudentSiblingView(Guid Id, string AdmissionNo, string Name, Guid CampusId, StudentStatus Status);

public record StudentDocumentView(Guid Id, string DocumentType, string FileName, string ContentType, long SizeBytes, DateOnly? ExpiresOn,
    DocumentVerificationStatus VerificationStatus, bool IsConfidential, DateTime UploadedAtUtc);

public record StudentDetail(Guid Id, string AdmissionNo, Guid CampusId, Guid? FamilyId, Guid? ApplicationId, string FirstName, string? LastName, string? NameBn,
    DateOnly DateOfBirth, string? Gender, string? BloodGroup, string? Religion, string? Nationality, string? BirthCertificateNo, string? NationalId, bool HasPhoto,
    string? PresentAddress, string? PermanentAddress, string? PreviousSchool, string? Languages, string? SpecialNeeds, string? Email, string? Phone, Guid? HouseId,
    string? RfidNo, DateOnly AdmissionDate, StudentStatus Status, DateOnly? LeftOn, Guid? CurrentEnrolmentId, IReadOnlyList<StudentGuardianView> Guardians,
    IReadOnlyList<StudentEnrolmentView> Enrolments, IReadOnlyList<StudentSiblingView> Siblings, IReadOnlyList<StudentDocumentView> Documents)
{
    public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}

public record StudentProfileRequest(string FirstName, string? LastName, string? NameBn, DateOnly DateOfBirth, string? Gender, string? BloodGroup, string? Religion,
    string? Nationality, string? BirthCertificateNo, string? NationalId, string? PresentAddress, string? PermanentAddress, string? PreviousSchool, string? Languages,
    string? SpecialNeeds, string? Email, string? Phone, Guid? HouseId, string? RfidNo);

public record StudentGuardianLinkRequest(Guid? GuardianId, string Name, string Relationship, string Phone, string? Email, string? NationalId, string? Occupation,
    bool IsPrimaryContact, bool IsAuthorisedPickup = true);

public record StudentGuardianFlagsRequest(string Relationship, bool IsPrimaryContact, bool IsAuthorisedPickup, bool IsEmergencyContact, bool CanViewFees);

public record CreateStudentRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, Guid? SectionId, DateOnly? AdmissionDate, StudentProfileRequest Profile,
    List<StudentGuardianLinkRequest> Guardians);

public record StudentTransferRequest(Guid TargetSectionId, DateOnly EffectiveDate, string Reason);

public record StudentExpiringDocument(Guid StudentId, string AdmissionNo, string StudentName, Guid DocumentId, string DocumentType, DateOnly ExpiresOn);

// ---- SIS-04 allocation & rolls

public enum StudentAllocationStrategy { Balanced = 0, Alphabetical = 1 }

public enum StudentRollOrder { Name = 0, AdmissionNo = 1, GenderThenName = 2 }

public record StudentBulkAllocationRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, StudentAllocationStrategy Strategy, bool BalanceGender,
    List<Guid>? StudentIds, bool OnlyUnallocated = true, bool DryRun = true);

public record StudentAllocationLine(Guid StudentId, string AdmissionNo, string Name, string? Gender, Guid? FromSectionId, Guid ToSectionId, string ToSectionName);

public record StudentBulkAllocationResult(bool Applied, IReadOnlyList<StudentAllocationLine> Lines, IReadOnlyDictionary<string, int> SectionTotals);

public record StudentRollGenerationRequest(Guid SectionId, StudentRollOrder Order, int StartFrom = 1);

// ---- SIS-05 promotion

public enum StudentPromotionOutcome { Promote = 0, Conditional = 1, Repeat = 2, Graduate = 3, Hold = 4 }

public record StudentPromotionCandidate(Guid StudentId, string AdmissionNo, string Name, Guid YearGroupId, string YearGroupName, Guid? SectionId, int? RollNo,
    StudentPromotionOutcome SuggestedOutcome, Guid? TargetYearGroupId, string? TargetYearGroupName, IReadOnlyList<string> Notes);

public record StudentPromotionDecision(Guid StudentId, StudentPromotionOutcome Outcome, Guid? TargetSectionId, string? Note);

public record StudentPromotionApplyRequest(Guid SourceAcademicYearId, Guid TargetAcademicYearId, List<StudentPromotionDecision> Decisions);

public record StudentPromotionApplyResult(int Promoted, int Conditional, int Repeated, int Graduated, int Held);

// ---- SIS-06 withdrawal & TC

public record StudentClearanceResult(string Area, bool Cleared, string Detail);

public record StudentWithdrawalView(Guid Id, Guid StudentId, string? AdmissionNo, string? StudentName, Guid CampusId, DateOnly LastAttendanceDate, string Reason,
    string? DestinationSchool, WithdrawalStatus Status, IReadOnlyList<StudentClearanceResult> Clearance, Guid? ApprovalRequestId, Guid? TransferCertificateId,
    DateTime CreatedAtUtc);

public record StudentWithdrawalCreateRequest(DateOnly LastAttendanceDate, string Reason, string? DestinationSchool);

public record StudentWithdrawalCompleteRequest(string? Conduct, string? Remarks);

public record StudentTransferCertificateView(Guid Id, string CertificateNo, Guid StudentId, string StudentName, string LastClass, DateOnly IssueDate, string? Conduct,
    string? Remarks, string VerificationCode);

// ---- SIS-08 ID cards

public record StudentIdCardData(Guid StudentId, string CardNo, string AdmissionNo, string Name, string? NameBn, string ClassName, string? Section, int? RollNo,
    string? BloodGroup, string CampusName, DateOnly ValidUntil, string? RfidNo, string QrPayload, bool HasPhoto);

public record StudentIssueCardsRequest(List<Guid>? StudentIds, Guid? SectionId, DateOnly ValidUntil);

public record StudentReasonBody(string Reason);

// ---- Guardian portal

public record GuardianPortalAccountResult(string UserName, string? TemporaryPassword);

// ---- CORE-10 import

public record StudentImportRowResult(int Row, string? Name, bool Valid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public record StudentImportResult(bool DryRun, int TotalRows, int ValidRows, int ImportedRows, IReadOnlyList<StudentImportRowResult> Rows);
