using SchoolManagement.Web.Models;

namespace SchoolManagement.Web.Pages.Students.Components;

/// <summary>Editable form state for <see cref="StudentProfileRequest"/>.</summary>
public sealed class StudentProfileModel
{
    public string FirstName { get; set; } = "";
    public string? LastName { get; set; }
    public string? NameBn { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? BloodGroup { get; set; }
    public string? Religion { get; set; }
    public string? Nationality { get; set; } = "Bangladeshi";
    public string? BirthCertificateNo { get; set; }
    public string? NationalId { get; set; }
    public string? PresentAddress { get; set; }
    public string? PermanentAddress { get; set; }
    public string? PreviousSchool { get; set; }
    public string? Languages { get; set; }
    public string? SpecialNeeds { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Guid? HouseId { get; set; }
    public string? RfidNo { get; set; }

    public static StudentProfileModel From(StudentDetail s) => new()
    {
        FirstName = s.FirstName, LastName = s.LastName, NameBn = s.NameBn, DateOfBirth = s.DateOfBirth.ToDateTime(TimeOnly.MinValue),
        Gender = s.Gender, BloodGroup = s.BloodGroup, Religion = s.Religion, Nationality = s.Nationality, BirthCertificateNo = s.BirthCertificateNo,
        NationalId = s.NationalId, PresentAddress = s.PresentAddress, PermanentAddress = s.PermanentAddress, PreviousSchool = s.PreviousSchool,
        Languages = s.Languages, SpecialNeeds = s.SpecialNeeds, Email = s.Email, Phone = s.Phone, HouseId = s.HouseId, RfidNo = s.RfidNo
    };

    public StudentProfileRequest ToRequest() => new(FirstName.Trim(), Blank(LastName), Blank(NameBn),
        DateOfBirth is { } d ? DateOnly.FromDateTime(d) : default, Blank(Gender), Blank(BloodGroup), Blank(Religion), Blank(Nationality),
        Blank(BirthCertificateNo), Blank(NationalId), Blank(PresentAddress), Blank(PermanentAddress), Blank(PreviousSchool), Blank(Languages),
        Blank(SpecialNeeds), Blank(Email), Blank(Phone), HouseId, Blank(RfidNo));

    public static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Editable form state for <see cref="StudentGuardianLinkRequest"/>.</summary>
public sealed class StudentGuardianModel
{
    public string Name { get; set; } = "";
    public string Relationship { get; set; } = "Father";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public string? Occupation { get; set; }
    public bool IsPrimaryContact { get; set; }
    public bool IsAuthorisedPickup { get; set; } = true;

    public StudentGuardianLinkRequest ToRequest() => new(null, Name.Trim(), Relationship.Trim(), Phone.Trim(), StudentProfileModel.Blank(Email),
        StudentProfileModel.Blank(NationalId), StudentProfileModel.Blank(Occupation), IsPrimaryContact, IsAuthorisedPickup);
}

public static class StudentChoices
{
    public static readonly string[] BloodGroups = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];
    public static readonly string[] Religions = ["Islam", "Hinduism", "Buddhism", "Christianity", "Other"];
    public static readonly string[] Relationships = ["Father", "Mother", "Grandfather", "Grandmother", "Uncle", "Aunt", "Brother", "Sister", "Guardian"];
    public static readonly string[] DocumentTypes =
        ["Birth certificate", "Photo", "Previous school TC", "Report card", "Vaccination card", "Passport", "Visa", "Medical report", "National ID (guardian)", "Other"];

    /// <summary>The TC can be issued once approved, or once cleared when no approval workflow applies.</summary>
    public static bool CanComplete(StudentWithdrawalView w) =>
        w.Status == Domain.Students.WithdrawalStatus.Approved ||
        (w.Status == Domain.Students.WithdrawalStatus.Cleared && w.ApprovalRequestId is null);

    public static bool IsOpen(StudentWithdrawalView w) =>
        w.Status is not (Domain.Students.WithdrawalStatus.Completed or Domain.Students.WithdrawalStatus.Rejected);

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB"
    };
}
