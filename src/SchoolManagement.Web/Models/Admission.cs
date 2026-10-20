using SchoolManagement.Domain.Admission;
using SchoolManagement.Domain.Core;

namespace SchoolManagement.Web.Models;

// Mirrors SchoolManagement.Api/Modules/Admission. Record names carry an Enquiry/Application/Admission prefix so they
// cannot clash with other modules; the JSON shapes (property names and types) match the API exactly.

// ---- Lookups (Core endpoints; only the fields the admission screens need) ----
public record AdmissionCampusOption(Guid Id, string Code, string Name, bool IsActive);
public record AdmissionYearOption(Guid Id, string Name, DateOnly StartDate, AcademicYearStatus Status, bool IsCurrent);
public record AdmissionClassOption(Guid Id, string Name, int Sequence);
public record AdmissionProgrammeOption(Guid Id, string Name, int Sequence, IReadOnlyList<AdmissionClassOption> YearGroups);
public record AdmissionSectionOption(Guid Id, string Name, int Capacity, int StudentCount);

/// <summary>A generic id/name pair for drop-downs (staff lookups and the public options).</summary>
public record AdmissionOption(Guid Id, string Name);

// ---- Enquiries (EnquiriesController) ----
public record EnquiryListItem(Guid Id, string EnquiryNo, Guid CampusId, EnquirySource Source, string ChildName, string GuardianName, string GuardianPhone,
    Guid? YearGroupId, EnquiryStage Stage, Guid? AssignedToUserId, DateTime CreatedAtUtc, DateTime? NextTaskDueUtc);
public record EnquiryActivityView(Guid Id, EnquiryActivityType Type, string Text, DateTime? DueAtUtc, bool IsCompleted, Guid? AssignedToUserId, DateTime CreatedAtUtc, Guid? CreatedBy);
public record EnquiryDetail(Guid Id, string EnquiryNo, Guid CampusId, EnquirySource Source, string? CampaignCode, Guid? AcademicYearId, Guid? YearGroupId,
    string ChildName, DateOnly? ChildDateOfBirth, string GuardianName, string GuardianPhone, string? GuardianEmail, string? Message, EnquiryStage Stage,
    Guid? AssignedToUserId, string? LostReason, Guid? ApplicationId, DateTime CreatedAtUtc, IReadOnlyList<EnquiryActivityView> Activities);
public record EnquiryRequest(Guid CampusId, EnquirySource Source, string? CampaignCode, Guid? AcademicYearId, Guid? YearGroupId, string ChildName,
    DateOnly? ChildDateOfBirth, string GuardianName, string GuardianPhone, string? GuardianEmail, string? Message);
public record EnquiryStageChangeRequest(EnquiryStage Stage, string? LostReason, string? Note);
public record EnquiryAssignRequest(Guid? UserId);
public record EnquiryActivityRequest(EnquiryActivityType Type, string Text, DateTime? DueAtUtc, Guid? AssignedToUserId, bool MarkCampusVisit = false);
public record EnquiryConvertRequest(Guid? AcademicYearId, Guid? YearGroupId);
public record EnquiryTaskItem(Guid ActivityId, Guid EnquiryId, string EnquiryNo, string ChildName, string GuardianPhone, string Text, DateTime? DueAtUtc, bool IsOverdue);
public record EnquiryFunnelRow(string Key, int Enquiries, int Applied, int Admitted, int Lost, decimal ConversionPercent);
public record EnquiryAnalyticsResult(EnquiryFunnelRow Total, IReadOnlyList<EnquiryFunnelRow> BySource, IReadOnlyList<EnquiryFunnelRow> ByCounsellor, IReadOnlyDictionary<string, int> ByStage);

// ---- Applications (ApplicationsController, ApplicationService, EnrolmentService) ----
public record ApplicationListItem(Guid Id, string ApplicationNo, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string Name, DateOnly DateOfBirth, string? Gender,
    ApplicationStatus Status, decimal? TestScore, decimal? MeritScore, int? MeritRank, bool ApplicationFeePaid, bool IsOnline, DateTime? SubmittedAtUtc, string? GuardianPhone);
public record ApplicationDocumentView(Guid Id, string DocumentType, string FileName, string ContentType, long SizeBytes, bool IsVerified, DateTime UploadedAtUtc);
public record ApplicationTestResultView(Guid TestSessionId, string SessionName, DateTime StartsAtUtc, string? Room, string AdmitCardNo, string? SeatNo, bool? Attended, decimal? Score, decimal MaxScore);
public record ApplicationInterviewView(Guid Id, DateTime ScheduledAtUtc, string? Location, string? InterviewerName, decimal? Score, string? Notes, bool IsCompleted);
public record ApplicationGuardianRequest(string Relationship, string Name, string Phone, string? Email, string? NationalId, string? Occupation,
    string? Organisation, string? IncomeBand, bool IsPrimaryContact);
public record ApplicationRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string FirstName, string? LastName, string? NameBn, DateOnly DateOfBirth,
    string? Gender, string? Religion, string? Nationality, string? BloodGroup, string? BirthCertificateNo, string? PreviousSchool, string? PreviousClass,
    string? PresentAddress, string? PermanentAddress, string? SpecialNeeds, bool HasSiblingInSchool, string? SiblingAdmissionNo, bool IsStaffWard,
    List<ApplicationGuardianRequest> Guardians);
public record ApplicationDetail(Guid Id, string ApplicationNo, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, Guid? EnquiryId, ApplicationStatus Status, bool IsOnline,
    string FirstName, string? LastName, string? NameBn, DateOnly DateOfBirth, string? Gender, string? Religion, string? Nationality, string? BloodGroup, string? BirthCertificateNo,
    string? PreviousSchool, string? PreviousClass, string? PresentAddress, string? PermanentAddress, string? SpecialNeeds, bool HasSiblingInSchool, Guid? SiblingStudentId, bool IsStaffWard,
    decimal ApplicationFee, bool ApplicationFeePaid, decimal? TestScore, decimal? InterviewScore, decimal? MeritScore, int? MeritRank, int? WaitlistPosition, string? DecisionNote,
    DateTime? SubmittedAtUtc, Guid? StudentId, IReadOnlyList<ApplicationGuardianRequest> Guardians, IReadOnlyList<ApplicationDocumentView> Documents,
    IReadOnlyList<ApplicationTestResultView> Tests, IReadOnlyList<ApplicationInterviewView> Interviews, IReadOnlyList<AdmissionOfferView> Offers, IReadOnlyList<AdmissionInvoiceView> Invoices)
{
    public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}
public record ApplicationFeeRequest(decimal Amount, string Channel, string Reference);
public record AdmissionReasonRequest(string Reason);
public record ApplicationInterviewRequest(DateTime ScheduledAtUtc, string? Location, string? InterviewerName, Guid? InterviewerUserId);
public record ApplicationInterviewResultRequest(decimal Score, string? Notes);
public record ApplicationEnrolRequest(Guid? SectionId, DateOnly? AdmissionDate, bool CreatePortalAccounts = true);
public record AdmissionPortalCredential(Guid GuardianId, string GuardianName, string UserName, string? TemporaryPassword);
public record ApplicationEnrolResult(Guid StudentId, string AdmissionNo, Guid EnrolmentId, Guid? SectionId, int? RollNo, Guid FamilyId, IReadOnlyList<AdmissionPortalCredential> PortalAccounts);

// ---- Admission tests (AdmissionTestsController) ----
public record AdmissionTestSessionView(Guid Id, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string Name, DateTime StartsAtUtc, int DurationMinutes, string? Room,
    string? InvigilatorName, int Capacity, decimal MaxScore, int Candidates, int Scored);
public record AdmissionTestSessionRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string Name, DateTime StartsAtUtc, int DurationMinutes, string? Room,
    string? InvigilatorName, Guid? InvigilatorUserId, int Capacity, decimal MaxScore = 100);
public record AdmissionCandidateView(Guid ApplicationId, string ApplicationNo, string Name, string AdmitCardNo, string? SeatNo, bool? Attended, decimal? Score, string? Remarks);
public record AdmissionAssignCandidatesRequest(List<Guid>? ApplicationIds);
public record AdmissionScoreEntry(Guid ApplicationId, bool Attended, decimal? Score, string? Remarks);
public record AdmissionAdmitCardView(string AdmitCardNo, string? SeatNo, string ApplicationNo, string Name, string ClassName, string CampusName, string SessionName,
    DateTime StartsAtUtc, int DurationMinutes, string? Room, string QrPayload);

// ---- Selection (SelectionController, SelectionService) ----
public record AdmissionClassKey(Guid CampusId, Guid AcademicYearId, Guid YearGroupId);
public record AdmissionSeatSummary(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string YearGroupName, int TotalSeats,
    int Enrolled, int AcceptedAwaitingEnrolment, int OffersPending, int Available, int Waitlisted);
public record AdmissionSeatRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, int TotalSeats);
public record AdmissionMeritEntry(Guid ApplicationId, string ApplicationNo, string Name, DateOnly DateOfBirth, decimal? TestScore, decimal? InterviewScore,
    bool Sibling, bool StaffWard, decimal? MeritScore, int? Rank, bool Eligible, IReadOnlyList<string> Notes, ApplicationStatus Status);
public record AdmissionFeeLine(Guid? FeeHeadId, string Description, decimal Amount);
public record AdmissionOfferOptions(int? ExpiresInDays, string? FeeScheduleText, List<AdmissionFeeLine>? FeeLines, string? Language);
public record AdmissionBatchOfferRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, int? MaxOffers, int? ExpiresInDays, string? FeeScheduleText,
    List<AdmissionFeeLine>? FeeLines, string? Language);
public record AdmissionOfferedItem(Guid ApplicationId, string OfferNo, DateTime ExpiresAtUtc);
public record AdmissionWaitlistItem(Guid ApplicationId, int Position);
public record AdmissionBatchOfferResult(IReadOnlyList<AdmissionOfferedItem> Offered, IReadOnlyList<AdmissionWaitlistItem> Waitlisted);
public record AdmissionWaitlistView(Guid Id, int Position, Guid ApplicationId, string ApplicationNo, string Name, decimal? MeritScore, int? MeritRank, DateTime AddedAtUtc);

// ---- Offers and invoices (OffersController, InvoicesController) ----
public record AdmissionOfferView(Guid Id, string OfferNo, Guid ApplicationId, string ApplicationNo, string ApplicantName, Guid CampusId, OfferStatus Status,
    DateTime IssuedAtUtc, DateTime ExpiresAtUtc, DateTime? RespondedAtUtc, string? ResponseNote, string? FeeScheduleText, IReadOnlyList<AdmissionFeeLine> FeeLines);
public record AdmissionOfferResponseRequest(bool Accept, string? Note);
public record AdmissionInvoiceView(Guid Id, string InvoiceNo, Guid ApplicationId, Guid CampusId, DateOnly DueDate, decimal Amount, AdmissionInvoiceStatus Status,
    DateTime? PaidAtUtc, string? PaymentChannel, string? PaymentReference, IReadOnlyList<AdmissionFeeLine> Lines);
public record AdmissionPaymentRequest(decimal Amount, string Channel, string Reference);

// ---- Public website (PublicAdmissionController) ----
public record AdmissionPublicItem(Guid Id, string Name, string? NameBn);
public record AdmissionPublicOptions(IReadOnlyList<AdmissionPublicItem> Campuses, IReadOnlyList<AdmissionPublicItem> Classes, IReadOnlyList<AdmissionPublicItem> AcademicYears);
public record AdmissionPublicEnquiryRequest(Guid CampusId, Guid? AcademicYearId, Guid? YearGroupId, string ChildName, DateOnly? ChildDateOfBirth, string GuardianName,
    string GuardianPhone, string? GuardianEmail, string? Message, string? CampaignCode, string? Website);
public record AdmissionPublicEnquiryResponse(string Reference);
public record AdmissionPublicApplicationRequest(ApplicationRequest Application, string? Website);
public record AdmissionPublicApplicationResponse(string ApplicationNo, string AccessToken, ApplicationStatus Status);
public record AdmissionPublicOfferView(string OfferNo, string ApplicantName, string? ClassName, string? CampusName, string? AcademicYear, OfferStatus Status,
    DateTime ExpiresAtUtc, string? FeeScheduleText);
