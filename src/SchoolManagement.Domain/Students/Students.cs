using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Students;

public enum StudentStatus
{
    Active = 0,
    Withdrawn = 1,
    Graduated = 2,
    Alumni = 3,
    Suspended = 4
}

/// <summary>Groups siblings and their guardians for one family login and combined fee view (SIS-02).</summary>
public class Family : SoftDeleteEntity
{
    public required string FamilyCode { get; set; }
    public string? Name { get; set; }
    public ICollection<Student> Students { get; set; } = [];
}

/// <summary>SIS-01: the single, authoritative student record.</summary>
[Audited]
public class Student : CampusEntity
{
    public required string AdmissionNo { get; set; }
    public Guid? FamilyId { get; set; }
    public Family? Family { get; set; }
    public Guid? ApplicationId { get; set; }
    public Guid? UserId { get; set; }

    public required string FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NameBn { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? BloodGroup { get; set; }
    public string? Religion { get; set; }
    public string? Nationality { get; set; }
    public string? BirthCertificateNo { get; set; }
    public string? NationalId { get; set; }
    public string? PhotoPath { get; set; }
    public string? PresentAddress { get; set; }
    public string? PermanentAddress { get; set; }
    public string? PreviousSchool { get; set; }
    public string? Languages { get; set; }
    public string? SpecialNeeds { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Guid? HouseId { get; set; }
    public string? RfidNo { get; set; }

    public DateOnly AdmissionDate { get; set; }
    public StudentStatus Status { get; set; } = StudentStatus.Active;
    public DateOnly? LeftOn { get; set; }

    /// <summary>Denormalised pointer to the current enrolment for fast lists; history lives in <see cref="Enrolments"/>.</summary>
    public Guid? CurrentEnrolmentId { get; set; }

    public ICollection<StudentEnrolment> Enrolments { get; set; } = [];
    public ICollection<StudentGuardian> Guardians { get; set; } = [];
    public ICollection<StudentDocument> Documents { get; set; } = [];

    public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}

[Audited]
public class Guardian : SoftDeleteEntity
{
    public Guid? FamilyId { get; set; }
    public Guid? UserId { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public required string Phone { get; set; }
    public string? AltPhone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public string? Occupation { get; set; }
    public string? Organisation { get; set; }
    public string? IncomeBand { get; set; }
    public string? Address { get; set; }
    public string? PhotoPath { get; set; }
    public ICollection<StudentGuardian> Students { get; set; } = [];
}

[Audited]
public class StudentGuardian : Entity
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid GuardianId { get; set; }
    public Guardian? Guardian { get; set; }
    public required string Relationship { get; set; }
    public bool IsPrimaryContact { get; set; }
    public bool IsAuthorisedPickup { get; set; } = true;
    public bool IsEmergencyContact { get; set; }
    public bool CanViewFees { get; set; } = true;
}

public enum EnrolmentStatus
{
    Active = 0,
    Promoted = 1,
    Repeated = 2,
    Transferred = 3,
    Withdrawn = 4,
    Graduated = 5
}

/// <summary>SIS-04: class/section/roll for one academic year; transfers close one row and open another so history is kept.</summary>
[Audited]
public class StudentEnrolment : CampusEntity, IAcademicYearScoped
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid AcademicYearId { get; set; }
    public Guid YearGroupId { get; set; }
    public Guid? SectionId { get; set; }
    public int? RollNo { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public EnrolmentStatus Status { get; set; } = EnrolmentStatus.Active;
    public string? Note { get; set; }
}

public enum DocumentVerificationStatus
{
    Pending = 0,
    Verified = 1,
    Rejected = 2
}

/// <summary>SIS-03: document vault entry; files live outside the web root and are only streamed after a permission check.</summary>
[Audited]
public class StudentDocument : AuditableEntity
{
    public Guid StudentId { get; set; }
    public required string DocumentType { get; set; }
    public required string FileName { get; set; }
    public required string StoragePath { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public DocumentVerificationStatus VerificationStatus { get; set; }
    public Guid? VerifiedBy { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public bool IsConfidential { get; set; }
}

/// <summary>SIS-08: issued ID card; reissue deactivates the previous card.</summary>
public class IdCard : AuditableEntity
{
    public Guid StudentId { get; set; }
    public required string CardNo { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateOnly ValidUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ReissueReason { get; set; }
    public decimal ReissueFee { get; set; }
}

public enum WithdrawalStatus
{
    Requested = 0,
    ClearancePending = 1,
    Cleared = 2,
    Approved = 3,
    Completed = 4,
    Rejected = 5
}

/// <summary>SIS-06: withdrawal workflow with dues clearance checks.</summary>
[Audited]
public class WithdrawalRequest : CampusEntity
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public DateOnly LastAttendanceDate { get; set; }
    public required string Reason { get; set; }
    public string? DestinationSchool { get; set; }
    public WithdrawalStatus Status { get; set; } = WithdrawalStatus.Requested;
    public string? ClearanceJson { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public Guid? TransferCertificateId { get; set; }
}

/// <summary>SIS-06: transfer certificate register entry.</summary>
[Audited]
public class TransferCertificate : CampusEntity
{
    public required string CertificateNo { get; set; }
    public Guid StudentId { get; set; }
    public Guid WithdrawalRequestId { get; set; }
    public DateOnly IssueDate { get; set; }
    public required string StudentName { get; set; }
    public required string LastClass { get; set; }
    public string? Conduct { get; set; }
    public string? Remarks { get; set; }
    public string VerificationCode { get; set; } = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
}
