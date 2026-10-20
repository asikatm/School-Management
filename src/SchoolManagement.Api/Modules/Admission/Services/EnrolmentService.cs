using SchoolManagement.Api.Modules.Identity;
using SchoolManagement.Api.Modules.Students.Services;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Domain.Students;
using SchoolManagement.Infrastructure.Numbering;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Admission.Services;

public record EnrolRequest(Guid? SectionId, DateOnly? AdmissionDate, bool CreatePortalAccounts = true);

public record PortalCredential(Guid GuardianId, string GuardianName, string UserName, string? TemporaryPassword);

public record EnrolResult(Guid StudentId, string AdmissionNo, Guid EnrolmentId, Guid? SectionId, int? RollNo, Guid FamilyId, IReadOnlyList<PortalCredential> PortalAccounts);

/// <summary>
/// ADM-08: one click turns an accepted applicant into a student — admission number, class, section, roll,
/// guardians (re-using existing family records for siblings), documents and parent portal accounts, without retyping.
/// </summary>
public sealed class EnrolmentService(
    AppDbContext db,
    INumberSequenceService numbers,
    ISettingsService settings,
    IGuardianAccountService guardianAccounts,
    ISectionAllocator allocator,
    AdmissionNotifier notifier,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<EnrolResult> EnrolAsync(Guid applicationId, EnrolRequest request, CancellationToken ct)
    {
        var app = await db.Applications.Include(a => a.Guardians).Include(a => a.Documents)
                      .FirstOrDefaultAsync(a => a.Id == applicationId, ct) ?? throw new NotFoundException("Application", applicationId);
        currentUser.EnsurePermission(Permissions.Students.Create, app.CampusId);

        if (app.Status != ApplicationStatus.Accepted)
            throw new DomainException("Only an application whose offer was accepted can be enrolled.", "invalid_status", isConflict: true);

        if (await settings.GetBoolAsync(SettingKeys.RequirePaymentBeforeEnrolment, true, app.CampusId, ct))
        {
            var unpaid = await db.AdmissionInvoices.AnyAsync(i => i.ApplicationId == app.Id && i.Status == AdmissionInvoiceStatus.Unpaid, ct);
            if (unpaid) throw new DomainException("The admission invoice must be paid before enrolment.", "invoice_unpaid", isConflict: true);
        }

        var today = DateOnly.FromDateTime(Infrastructure.Localization.BanglaFormat.ToDhaka(clock.GetUtcNow().UtcDateTime));
        var family = await ResolveFamilyAsync(app, ct);

        var student = new Student
        {
            AdmissionNo = await numbers.NextAsync(NumberKeys.AdmissionNo, app.CampusId, app.AcademicYearId, ct),
            CampusId = app.CampusId,
            FamilyId = family.Id,
            ApplicationId = app.Id,
            FirstName = app.FirstName,
            LastName = app.LastName,
            NameBn = app.NameBn,
            DateOfBirth = app.DateOfBirth,
            Gender = app.Gender,
            Religion = app.Religion,
            Nationality = app.Nationality,
            BloodGroup = app.BloodGroup,
            BirthCertificateNo = app.BirthCertificateNo,
            PresentAddress = app.PresentAddress,
            PermanentAddress = app.PermanentAddress,
            PreviousSchool = app.PreviousSchool,
            SpecialNeeds = app.SpecialNeeds,
            AdmissionDate = request.AdmissionDate ?? today
        };
        db.Students.Add(student);

        var sectionId = request.SectionId ?? await allocator.PickSectionAsync(app.CampusId, app.AcademicYearId, app.YearGroupId, student.Gender, ct);
        if (sectionId is { } sid && !await db.Sections.AnyAsync(s => s.Id == sid && s.CampusId == app.CampusId && s.AcademicYearId == app.AcademicYearId && s.YearGroupId == app.YearGroupId, ct))
            throw new DomainException("The section does not belong to the applicant's campus, year and class.", "section_mismatch");

        var enrolment = new StudentEnrolment
        {
            StudentId = student.Id,
            CampusId = app.CampusId,
            AcademicYearId = app.AcademicYearId,
            YearGroupId = app.YearGroupId,
            SectionId = sectionId,
            RollNo = sectionId is { } s ? await allocator.NextRollAsync(s, ct) : null,
            StartDate = student.AdmissionDate
        };
        db.StudentEnrolments.Add(enrolment);
        student.CurrentEnrolmentId = enrolment.Id;

        var guardians = new List<Guardian>();
        foreach (var g in app.Guardians)
        {
            var guardian = await FindOrCreateGuardianAsync(g, family.Id, ct);
            guardians.Add(guardian);
            db.StudentGuardians.Add(new StudentGuardian
            {
                StudentId = student.Id, GuardianId = guardian.Id, Relationship = g.Relationship,
                IsPrimaryContact = g.IsPrimaryContact, IsEmergencyContact = g.IsPrimaryContact
            });
        }

        foreach (var d in app.Documents)
            db.StudentDocuments.Add(new StudentDocument
            {
                StudentId = student.Id, DocumentType = d.DocumentType, FileName = d.FileName, StoragePath = d.StoragePath,
                ContentType = d.ContentType, SizeBytes = d.SizeBytes,
                VerificationStatus = d.IsVerified ? DocumentVerificationStatus.Verified : DocumentVerificationStatus.Pending
            });

        app.Status = ApplicationStatus.Enrolled;
        app.StudentId = student.Id;
        if (app.EnquiryId is not null)
            await db.Enquiries.Where(e => e.Id == app.EnquiryId).ForEachAsync(e => e.Stage = EnquiryStage.Admitted, ct);

        var credentials = new List<PortalCredential>();
        if (request.CreatePortalAccounts)
        {
            foreach (var guardian in guardians)
            {
                var (userName, password) = await guardianAccounts.EnsureAccountAsync(guardian, app.CampusId, ct);
                credentials.Add(new PortalCredential(guardian.Id, guardian.Name, userName, password));
            }
        }

        await db.SaveChangesAsync(ct);

        var primaryPhone = app.Guardians.FirstOrDefault(x => x.IsPrimaryContact)?.Phone;
        var primary = credentials.FirstOrDefault(c => guardians.Any(g => g.Id == c.GuardianId && g.Phone == primaryPhone))
                      ?? credentials.FirstOrDefault();
        var portalUrl = await settings.GetAsync(SettingKeys.SchoolPortalUrl, app.CampusId, ct);
        await notifier.NotifyAsync(app, TemplateCodes.EnrolmentWelcome, new Dictionary<string, string?>
        {
            ["AdmissionNo"] = student.AdmissionNo,
            ["UserName"] = primary?.UserName ?? "-",
            ["Password"] = primary?.TemporaryPassword ?? "(existing password)",
            ["PortalUrl"] = portalUrl
        }, ct);
        await db.SaveChangesAsync(ct);

        // TODO(FEE-01, release 2): create the student's fee plan from the fee structure here.
        return new EnrolResult(student.Id, student.AdmissionNo, enrolment.Id, enrolment.SectionId, enrolment.RollNo, family.Id, credentials);
    }

    /// <summary>Re-uses the sibling's family, or any family a guardian (matched by phone) already belongs to.</summary>
    private async Task<Family> ResolveFamilyAsync(Application app, CancellationToken ct)
    {
        Guid? familyId = null;
        if (app.SiblingStudentId is { } siblingId)
            familyId = await db.Students.IgnoreQueryFilters().Where(s => s.Id == siblingId).Select(s => s.FamilyId).FirstOrDefaultAsync(ct);

        if (familyId is null)
        {
            var phones = app.Guardians.Select(g => g.Phone).ToList();
            familyId = await db.Guardians.Where(g => phones.Contains(g.Phone) && g.FamilyId != null).Select(g => g.FamilyId).FirstOrDefaultAsync(ct);
        }

        if (familyId is not null)
            return await db.Families.FirstAsync(f => f.Id == familyId, ct);

        var family = new Family
        {
            FamilyCode = await numbers.NextAsync(NumberKeys.Family, ct: ct),
            Name = app.Guardians.FirstOrDefault(g => g.IsPrimaryContact)?.Name ?? app.LastName
        };
        db.Families.Add(family);
        return family;
    }

    private async Task<Guardian> FindOrCreateGuardianAsync(ApplicationGuardian g, Guid familyId, CancellationToken ct)
    {
        var existing = await db.Guardians.FirstOrDefaultAsync(x => x.Phone == g.Phone && (x.FamilyId == familyId || x.NationalId == g.NationalId), ct)
                       ?? db.Guardians.Local.FirstOrDefault(x => x.Phone == g.Phone);
        if (existing is not null)
        {
            existing.FamilyId ??= familyId;
            existing.Email ??= g.Email;
            existing.NationalId ??= g.NationalId;
            return existing;
        }

        var guardian = new Guardian
        {
            FamilyId = familyId, Name = g.Name, Phone = g.Phone, Email = g.Email, NationalId = g.NationalId,
            Occupation = g.Occupation, Organisation = g.Organisation, IncomeBand = g.IncomeBand
        };
        db.Guardians.Add(guardian);
        return guardian;
    }
}
