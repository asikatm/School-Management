using SchoolManagement.Web.Models;

namespace SchoolManagement.Web.Pages.Admission;

/// <summary>Editable model behind <see cref="ApplicationEditor"/>; converts to and from the API records.</summary>
public sealed class ApplicationForm
{
    public Guid? CampusId { get; set; }
    public Guid? AcademicYearId { get; set; }
    public Guid? YearGroupId { get; set; }
    public string FirstName { get; set; } = "";
    public string? LastName { get; set; }
    public string? NameBn { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Religion { get; set; }
    public string? Nationality { get; set; } = "Bangladeshi";
    public string? BloodGroup { get; set; }
    public string? BirthCertificateNo { get; set; }
    public string? PreviousSchool { get; set; }
    public string? PreviousClass { get; set; }
    public string? PresentAddress { get; set; }
    public string? PermanentAddress { get; set; }
    public string? SpecialNeeds { get; set; }
    public bool HasSiblingInSchool { get; set; }
    public string? SiblingAdmissionNo { get; set; }
    public bool IsStaffWard { get; set; }
    public List<GuardianForm> Guardians { get; set; } = [new() { Relationship = "Father", IsPrimaryContact = true }];

    public bool IsComplete => CampusId is not null && AcademicYearId is not null && YearGroupId is not null &&
                              !string.IsNullOrWhiteSpace(FirstName) && DateOfBirth is not null;

    public ApplicationRequest ToRequest() => new(CampusId ?? Guid.Empty, AcademicYearId ?? Guid.Empty, YearGroupId ?? Guid.Empty,
        FirstName.Trim(), Blank(LastName), Blank(NameBn), DateOnly.FromDateTime(DateOfBirth ?? DateTime.MinValue), Blank(Gender), Blank(Religion), Blank(Nationality),
        Blank(BloodGroup), Blank(BirthCertificateNo), Blank(PreviousSchool), Blank(PreviousClass), Blank(PresentAddress), Blank(PermanentAddress),
        Blank(SpecialNeeds), HasSiblingInSchool, HasSiblingInSchool ? Blank(SiblingAdmissionNo) : null, IsStaffWard,
        Guardians.Select(g => g.ToRequest()).ToList());

    public static ApplicationForm From(ApplicationDetail a) => new()
    {
        CampusId = a.CampusId, AcademicYearId = a.AcademicYearId, YearGroupId = a.YearGroupId, FirstName = a.FirstName, LastName = a.LastName,
        NameBn = a.NameBn, DateOfBirth = a.DateOfBirth == default ? null : a.DateOfBirth.ToDateTime(TimeOnly.MinValue), Gender = a.Gender,
        Religion = a.Religion, Nationality = a.Nationality, BloodGroup = a.BloodGroup, BirthCertificateNo = a.BirthCertificateNo,
        PreviousSchool = a.PreviousSchool, PreviousClass = a.PreviousClass, PresentAddress = a.PresentAddress, PermanentAddress = a.PermanentAddress,
        SpecialNeeds = a.SpecialNeeds, HasSiblingInSchool = a.HasSiblingInSchool, IsStaffWard = a.IsStaffWard,
        Guardians = a.Guardians.Select(GuardianForm.From).ToList()
    };

    internal static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed class GuardianForm
{
    public string Relationship { get; set; } = "Guardian";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public string? Occupation { get; set; }
    public string? Organisation { get; set; }
    public string? IncomeBand { get; set; }
    public bool IsPrimaryContact { get; set; }

    public ApplicationGuardianRequest ToRequest() => new(Relationship, Name.Trim(), Phone.Trim(), ApplicationForm.Blank(Email), ApplicationForm.Blank(NationalId),
        ApplicationForm.Blank(Occupation), ApplicationForm.Blank(Organisation), ApplicationForm.Blank(IncomeBand), IsPrimaryContact);

    public static GuardianForm From(ApplicationGuardianRequest g) => new()
    {
        Relationship = g.Relationship, Name = g.Name, Phone = g.Phone, Email = g.Email, NationalId = g.NationalId,
        Occupation = g.Occupation, Organisation = g.Organisation, IncomeBand = g.IncomeBand, IsPrimaryContact = g.IsPrimaryContact
    };
}

public sealed class FeeLineForm
{
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
}

/// <summary>Result of <see cref="OfferTermsDialog"/>: the offer options and, for batch offers, the maximum number of offers.</summary>
public sealed record OfferTerms(int? MaxOffers, AdmissionOfferOptions Options);
