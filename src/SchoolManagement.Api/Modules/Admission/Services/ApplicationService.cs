using System.Security.Cryptography;
using System.Text;
using SchoolManagement.Api.Modules.Students.Services;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Infrastructure.Numbering;
using SchoolManagement.Infrastructure.Storage;

namespace SchoolManagement.Api.Modules.Admission.Services;

public record ApplicationGuardianRequest(string Relationship, string Name, string Phone, string? Email, string? NationalId, string? Occupation,
    string? Organisation, string? IncomeBand, bool IsPrimaryContact);

public record ApplicationRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string FirstName, string? LastName, string? NameBn, DateOnly DateOfBirth,
    string? Gender, string? Religion, string? Nationality, string? BloodGroup, string? BirthCertificateNo, string? PreviousSchool, string? PreviousClass,
    string? PresentAddress, string? PermanentAddress, string? SpecialNeeds, bool HasSiblingInSchool, string? SiblingAdmissionNo, bool IsStaffWard,
    List<ApplicationGuardianRequest> Guardians);

/// <summary>ADM-03: shared by staff screens and the public online form (draft-save, documents, submission).</summary>
public sealed class ApplicationService(
    AppDbContext db,
    INumberSequenceService numbers,
    IFileStorage storage,
    AdmissionNotifier notifier,
    TimeProvider clock)
{
    public static readonly ApplicationStatus[] EditableStatuses = [ApplicationStatus.Draft, ApplicationStatus.Submitted, ApplicationStatus.UnderReview];

    /// <summary>Creates a draft. For the public form, returns the secret the guardian needs to resume it.</summary>
    public async Task<(Application App, string? AccessToken)> CreateAsync(ApplicationRequest r, bool online, CancellationToken ct)
    {
        await EnsureOpenForAdmissionAsync(r, ct);
        var app = new Application
        {
            ApplicationNo = await numbers.NextAsync(NumberKeys.Application, r.CampusId, r.AcademicYearId, ct),
            CampusId = r.CampusId, AcademicYearId = r.AcademicYearId, YearGroupId = r.YearGroupId, FirstName = r.FirstName,
            IsOnline = online
        };
        await ApplyAsync(app, r, ct);
        string? token = null;
        if (online)
        {
            token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
            app.AccessTokenHash = HashToken(token);
        }
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        return (app, token);
    }

    public async Task UpdateAsync(Application app, ApplicationRequest r, CancellationToken ct)
    {
        if (!EditableStatuses.Contains(app.Status))
            throw new DomainException($"An application in status {app.Status} can no longer be edited.", "not_editable", isConflict: true);
        if (app.CampusId != r.CampusId || app.AcademicYearId != r.AcademicYearId || app.YearGroupId != r.YearGroupId)
            await EnsureOpenForAdmissionAsync(r, ct);

        app.CampusId = r.CampusId;
        app.AcademicYearId = r.AcademicYearId;
        app.YearGroupId = r.YearGroupId;
        db.ApplicationGuardians.RemoveRange(app.Guardians);
        app.Guardians.Clear();
        await ApplyAsync(app, r, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ApplicationDocument> AddDocumentAsync(Application app, IFormFile file, string documentType, CancellationToken ct)
    {
        if (app.Status is not (ApplicationStatus.Draft or ApplicationStatus.Submitted or ApplicationStatus.UnderReview or ApplicationStatus.Offered or ApplicationStatus.Accepted))
            throw new DomainException("Documents can no longer be added to this application.", "not_editable", isConflict: true);
        if (app.Documents.Count >= 15) throw new DomainException("At most 15 documents per application.", "too_many_documents");

        await using var stream = file.OpenReadStream();
        var stored = await storage.SaveAsync(stream, $"applications/{app.Id:N}", file.FileName, file.ContentType, ct);
        var doc = new ApplicationDocument
        {
            ApplicationId = app.Id, DocumentType = documentType, FileName = stored.FileName, StoragePath = stored.Path,
            ContentType = stored.ContentType, SizeBytes = stored.SizeBytes
        };
        db.ApplicationDocuments.Add(doc);
        await db.SaveChangesAsync(ct);
        return doc;
    }

    public async Task SubmitAsync(Application app, CancellationToken ct)
    {
        if (app.Status != ApplicationStatus.Draft)
            throw new DomainException("Only a draft can be submitted.", "invalid_status", isConflict: true);

        var problems = new List<string>();
        if (app.DateOfBirth == default) problems.Add("Date of birth is required.");
        if (app.Guardians.Count == 0) problems.Add("At least one guardian is required.");
        if (app.Guardians.Count(g => g.IsPrimaryContact) != 1) problems.Add("Exactly one guardian must be the primary contact.");
        if (problems.Count > 0) throw new DomainException(string.Join(" ", problems), "incomplete");

        app.Status = ApplicationStatus.Submitted;
        app.SubmittedAtUtc = clock.GetUtcNow().UtcDateTime;
        await notifier.NotifyAsync(app, TemplateCodes.ApplicationSubmitted, [], ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Application?> FindByTokenAsync(string token, CancellationToken ct)
    {
        var hash = HashToken(token);
        return await db.Applications.Include(a => a.Guardians).Include(a => a.Documents).FirstOrDefaultAsync(a => a.AccessTokenHash == hash, ct);
    }

    private async Task ApplyAsync(Application app, ApplicationRequest r, CancellationToken ct)
    {
        app.FirstName = r.FirstName.Trim();
        app.LastName = r.LastName?.Trim();
        app.NameBn = r.NameBn;
        app.DateOfBirth = r.DateOfBirth;
        app.Gender = r.Gender;
        app.Religion = r.Religion;
        app.Nationality = r.Nationality;
        app.BloodGroup = r.BloodGroup;
        app.BirthCertificateNo = r.BirthCertificateNo;
        app.PreviousSchool = r.PreviousSchool;
        app.PreviousClass = r.PreviousClass;
        app.PresentAddress = r.PresentAddress;
        app.PermanentAddress = r.PermanentAddress;
        app.SpecialNeeds = r.SpecialNeeds;
        app.IsStaffWard = r.IsStaffWard;
        app.HasSiblingInSchool = r.HasSiblingInSchool;
        app.SiblingStudentId = null;

        if (r.HasSiblingInSchool && !string.IsNullOrWhiteSpace(r.SiblingAdmissionNo))
        {
            // Sibling may study at another campus, so look across campuses (read-only lookup by admission number).
            app.SiblingStudentId = await db.Students.IgnoreQueryFilters()
                .Where(s => s.AdmissionNo == r.SiblingAdmissionNo && !s.IsDeleted && s.Status == Domain.Students.StudentStatus.Active)
                .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (app.SiblingStudentId is null)
                throw new DomainException($"No active student with admission number {r.SiblingAdmissionNo}.", "sibling_not_found");
        }

        foreach (var g in r.Guardians)
            app.Guardians.Add(new ApplicationGuardian
            {
                ApplicationId = app.Id, Relationship = g.Relationship, Name = g.Name.Trim(),
                Phone = StudentImportService.NormalisePhone(g.Phone) ?? g.Phone, Email = g.Email, NationalId = g.NationalId,
                Occupation = g.Occupation, Organisation = g.Organisation, IncomeBand = g.IncomeBand, IsPrimaryContact = g.IsPrimaryContact
            });
    }

    private async Task EnsureOpenForAdmissionAsync(ApplicationRequest r, CancellationToken ct)
    {
        var year = await db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == r.AcademicYearId, ct) ?? throw new NotFoundException("Academic year", r.AcademicYearId);
        if (!year.IsWritable) throw new DomainException($"Admissions for {year.Name} are closed.", "year_locked", isConflict: true);
        if (!await db.Campuses.AnyAsync(c => c.Id == r.CampusId && c.IsActive, ct)) throw new NotFoundException("Campus", r.CampusId);
        if (!await db.YearGroups.AnyAsync(y => y.Id == r.YearGroupId, ct)) throw new NotFoundException("Year group", r.YearGroupId);
    }

    private static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class ApplicationRequestValidator : AbstractValidator<ApplicationRequest>
{
    public ApplicationRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).MaximumLength(100);
        RuleFor(x => x.DateOfBirth).Must(d => d < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) && d.Year > 1990)
            .WithMessage("Enter a valid date of birth.");
        RuleFor(x => x.Gender).Must(g => g is null or "Male" or "Female").WithMessage("Gender must be Male or Female.");
        RuleFor(x => x.BloodGroup).Must(b => b is null or "A+" or "A-" or "B+" or "B-" or "AB+" or "AB-" or "O+" or "O-");
        RuleFor(x => x.BirthCertificateNo).Matches(@"^\d{17}$").When(x => !string.IsNullOrEmpty(x.BirthCertificateNo))
            .WithMessage("Birth registration number must be 17 digits.");
        RuleFor(x => x.SiblingAdmissionNo).NotEmpty().When(x => x.HasSiblingInSchool).WithMessage("Enter the sibling's admission number.");
        RuleFor(x => x.Guardians).NotNull().Must(g => g.Count <= 4).WithMessage("At most 4 guardians.");
        RuleForEach(x => x.Guardians).ChildRules(g =>
        {
            g.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            g.RuleFor(x => x.Relationship).NotEmpty().MaximumLength(32);
            g.RuleFor(x => x.Phone).Must(p => StudentImportService.NormalisePhone(p) is not null).WithMessage("Enter a valid Bangladeshi mobile number.");
            g.RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
            g.RuleFor(x => x.NationalId).Matches(@"^(\d{10}|\d{13}|\d{17})$").When(x => !string.IsNullOrEmpty(x.NationalId))
                .WithMessage("NID must be 10, 13 or 17 digits.");
        });
    }
}
