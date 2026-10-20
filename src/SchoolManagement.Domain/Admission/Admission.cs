using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Admission;

public enum EnquirySource
{
    Website = 0,
    Phone = 1,
    WalkIn = 2,
    SocialMedia = 3,
    Campaign = 4,
    Referral = 5,
    Other = 6
}

/// <summary>ADM-02 pipeline stages.</summary>
public enum EnquiryStage
{
    Enquiry = 0,
    CampusVisit = 1,
    Applied = 2,
    TestInterview = 3,
    Offered = 4,
    Admitted = 5,
    Lost = 6
}

/// <summary>ADM-01: every enquiry lands in one inbox with source and campus/class interest.</summary>
[Audited]
public class Enquiry : CampusEntity
{
    public required string EnquiryNo { get; set; }
    public EnquirySource Source { get; set; }
    public string? CampaignCode { get; set; }
    public Guid? AcademicYearId { get; set; }
    public Guid? YearGroupId { get; set; }
    public required string ChildName { get; set; }
    public DateOnly? ChildDateOfBirth { get; set; }
    public required string GuardianName { get; set; }
    public required string GuardianPhone { get; set; }
    public string? GuardianEmail { get; set; }
    public string? Message { get; set; }
    public EnquiryStage Stage { get; set; } = EnquiryStage.Enquiry;
    public Guid? AssignedToUserId { get; set; }
    public string? LostReason { get; set; }
    public Guid? ApplicationId { get; set; }
    public ICollection<EnquiryActivity> Activities { get; set; } = [];
}

public enum EnquiryActivityType
{
    Note = 0,
    Call = 1,
    Task = 2,
    StageChange = 3,
    Email = 4,
    Sms = 5
}

/// <summary>ADM-02: call notes, tasks and reminders against an enquiry.</summary>
public class EnquiryActivity : AuditableEntity
{
    public Guid EnquiryId { get; set; }
    public EnquiryActivityType Type { get; set; }
    public required string Text { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public bool IsCompleted { get; set; }
    public Guid? AssignedToUserId { get; set; }
}

public enum ApplicationStatus
{
    Draft = 0,
    Submitted = 1,
    UnderReview = 2,
    TestScheduled = 3,
    Tested = 4,
    Shortlisted = 5,
    Waitlisted = 6,
    Offered = 7,
    Accepted = 8,
    Declined = 9,
    Rejected = 10,
    Enrolled = 11,
    Withdrawn = 12
}

/// <summary>ADM-03: online application with draft-save, documents and guardian details.</summary>
[Audited]
public class Application : CampusEntity, IAcademicYearScoped
{
    public required string ApplicationNo { get; set; }
    public Guid AcademicYearId { get; set; }
    public Guid YearGroupId { get; set; }
    public Guid? EnquiryId { get; set; }
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Draft;

    public required string FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NameBn { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Religion { get; set; }
    public string? Nationality { get; set; }
    public string? BloodGroup { get; set; }
    public string? BirthCertificateNo { get; set; }
    public string? PreviousSchool { get; set; }
    public string? PreviousClass { get; set; }
    public string? PresentAddress { get; set; }
    public string? PermanentAddress { get; set; }
    public string? SpecialNeeds { get; set; }

    /// <summary>Priority flags used by selection rules (ADM-05).</summary>
    public bool HasSiblingInSchool { get; set; }
    public Guid? SiblingStudentId { get; set; }
    public bool IsStaffWard { get; set; }

    public decimal ApplicationFee { get; set; }
    public bool ApplicationFeePaid { get; set; }
    public string? ApplicationFeeReference { get; set; }

    public decimal? TestScore { get; set; }
    public decimal? InterviewScore { get; set; }
    public decimal? MeritScore { get; set; }
    public int? MeritRank { get; set; }
    public string? DecisionNote { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? StudentId { get; set; }

    /// <summary>SHA-256 of the secret that lets a guardian resume a draft from the public form; null for staff-created applications.</summary>
    public string? AccessTokenHash { get; set; }
    public bool IsOnline { get; set; }

    public ICollection<ApplicationGuardian> Guardians { get; set; } = [];
    public ICollection<ApplicationDocument> Documents { get; set; } = [];

    public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}

public class ApplicationGuardian : Entity
{
    public Guid ApplicationId { get; set; }
    public required string Relationship { get; set; }
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public string? Occupation { get; set; }
    public string? Organisation { get; set; }
    public string? IncomeBand { get; set; }
    public bool IsPrimaryContact { get; set; }
}

public class ApplicationDocument : AuditableEntity
{
    public Guid ApplicationId { get; set; }
    public required string DocumentType { get; set; }
    public required string FileName { get; set; }
    public required string StoragePath { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public bool IsVerified { get; set; }
}

/// <summary>Seats offered per class and campus for a year (ADM-05 seat availability).</summary>
public class SeatCapacity : CampusEntity, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid YearGroupId { get; set; }
    public int TotalSeats { get; set; }
}

/// <summary>ADM-04: admission test session per class.</summary>
public class TestSession : CampusEntity, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid YearGroupId { get; set; }
    public required string Name { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public string? Room { get; set; }
    public string? InvigilatorName { get; set; }
    public Guid? InvigilatorUserId { get; set; }
    public int Capacity { get; set; } = 30;
    public decimal MaxScore { get; set; } = 100;
    public ICollection<TestCandidate> Candidates { get; set; } = [];
}

public class TestCandidate : Entity
{
    public Guid TestSessionId { get; set; }
    public Guid ApplicationId { get; set; }
    public Application? Application { get; set; }
    public required string AdmitCardNo { get; set; }
    public string? SeatNo { get; set; }
    public bool? Attended { get; set; }
    public decimal? Score { get; set; }
    public string? Remarks { get; set; }
}

public class Interview : AuditableEntity
{
    public Guid ApplicationId { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    public string? Location { get; set; }
    public string? InterviewerName { get; set; }
    public Guid? InterviewerUserId { get; set; }
    public decimal? Score { get; set; }
    public string? Notes { get; set; }
    public bool IsCompleted { get; set; }
}

public class WaitlistEntry : CampusEntity, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid YearGroupId { get; set; }
    public Guid ApplicationId { get; set; }
    public Application? Application { get; set; }
    public int Position { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? PromotedAtUtc { get; set; }
}

public enum OfferStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Expired = 3,
    Revoked = 4
}

/// <summary>ADM-06: offer with deadline; an expired offer releases its seat automatically.</summary>
[Audited]
public class Offer : CampusEntity
{
    public required string OfferNo { get; set; }
    public Guid ApplicationId { get; set; }
    public Application? Application { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public string? FeeScheduleText { get; set; }

    /// <summary>Fee lines (JSON) quoted in the offer; they become the admission invoice on acceptance (ADM-07).</summary>
    public string? FeeLinesJson { get; set; }
    public string? Language { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public string? ResponseNote { get; set; }

    /// <summary>Unguessable token the guardian uses to accept or decline online.</summary>
    public required string ResponseToken { get; set; }
}

public enum AdmissionInvoiceStatus
{
    Unpaid = 0,
    Paid = 1,
    Cancelled = 2
}

/// <summary>
/// ADM-07: admission / session fee raised on acceptance. Kept minimal here; the FEE module
/// (release 2) takes over invoicing and posts these to the ledger.
/// </summary>
[Audited]
public class AdmissionInvoice : CampusEntity
{
    public required string InvoiceNo { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid? OfferId { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public AdmissionInvoiceStatus Status { get; set; } = AdmissionInvoiceStatus.Unpaid;
    public DateTime? PaidAtUtc { get; set; }
    public string? PaymentChannel { get; set; }
    public string? PaymentReference { get; set; }
    public ICollection<AdmissionInvoiceLine> Lines { get; set; } = [];
}

public class AdmissionInvoiceLine : Entity
{
    public Guid InvoiceId { get; set; }
    public Guid? FeeHeadId { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
}
