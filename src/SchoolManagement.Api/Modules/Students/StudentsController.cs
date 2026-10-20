using SchoolManagement.Api.Modules.Students.Services;
using SchoolManagement.Domain.Audit;
using SchoolManagement.Domain.Students;
using SchoolManagement.Infrastructure.Auditing;
using SchoolManagement.Infrastructure.Numbering;
using SchoolManagement.Infrastructure.Storage;

namespace SchoolManagement.Api.Modules.Students;

/// <summary>SIS-01 student profile, SIS-02 guardians &amp; family, SIS-03 document vault, SIS-04 transfers.</summary>
[ApiController]
[Route("api/students")]
[Tags("Students")]
public sealed class StudentsController(
    AppDbContext db,
    INumberSequenceService numbers,
    IFileStorage storage,
    ISectionAllocator allocator,
    IAuditLog audit,
    ICurrentUser currentUser,
    TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Students.View)]
    public async Task<PagedResult<StudentListItem>> List([FromQuery] StudentQuery query, [FromQuery] PageQuery page, CancellationToken ct) =>
        await StudentQueries.Filter(db, query).OrderBy(x => x.ClassSequence).ThenBy(x => x.Section).ThenBy(x => x.RollNo).ThenBy(x => x.FirstName)
            .Select(x => new StudentListItem(x.Id, x.AdmissionNo, x.FirstName + " " + (x.LastName ?? ""), x.NameBn, x.Gender, x.DateOfBirth, x.CampusId,
                x.YearGroupId, x.ClassName, x.SectionId, x.Section, x.RollNo, x.Status, x.GuardianPhone))
            .ToPagedAsync(page, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Students.View)]
    public async Task<StudentDetail> Get(Guid id, CancellationToken ct)
    {
        var s = await db.Students.AsNoTracking()
            .Include(x => x.Guardians).ThenInclude(g => g.Guardian)
            .Include(x => x.Enrolments)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);

        var yearGroups = await db.YearGroups.AsNoTracking().ToDictionaryAsync(y => y.Id, y => y.Name, ct);
        var years = await db.AcademicYears.AsNoTracking().ToDictionaryAsync(y => y.Id, y => y.Name, ct);
        var sectionIds = s.Enrolments.Where(e => e.SectionId != null).Select(e => e.SectionId!.Value).ToList();
        var sections = await db.Sections.IgnoreQueryFilters().AsNoTracking().Where(x => sectionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var siblings = s.FamilyId is null ? [] : await db.Students.AsNoTracking()
            .Where(x => x.FamilyId == s.FamilyId && x.Id != s.Id)
            .Select(x => new SiblingView(x.Id, x.AdmissionNo, x.FirstName + " " + (x.LastName ?? ""), x.CampusId, x.Status)).ToListAsync(ct);
        var docs = await db.StudentDocuments.AsNoTracking().Where(d => d.StudentId == id)
            .Where(d => !d.IsConfidential || currentUser.HasPermission(Permissions.Students.Documents, s.CampusId))
            .OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => new StudentDocumentView(d.Id, d.DocumentType, d.FileName, d.ContentType, d.SizeBytes, d.ExpiresOn, d.VerificationStatus, d.IsConfidential, d.CreatedAtUtc))
            .ToListAsync(ct);

        return new StudentDetail(s.Id, s.AdmissionNo, s.CampusId, s.FamilyId, s.ApplicationId, s.FirstName, s.LastName, s.NameBn, s.DateOfBirth, s.Gender, s.BloodGroup,
            s.Religion, s.Nationality, s.BirthCertificateNo, s.NationalId, s.PhotoPath is not null, s.PresentAddress, s.PermanentAddress, s.PreviousSchool, s.Languages,
            s.SpecialNeeds, s.Email, s.Phone, s.HouseId, s.RfidNo, s.AdmissionDate, s.Status, s.LeftOn, s.CurrentEnrolmentId,
            s.Guardians.Select(g => new StudentGuardianView(g.GuardianId, g.Guardian!.Name, g.Guardian.NameBn, g.Relationship, g.Guardian.Phone, g.Guardian.Email,
                g.Guardian.Occupation, g.IsPrimaryContact, g.IsAuthorisedPickup, g.IsEmergencyContact, g.CanViewFees, g.Guardian.UserId is not null)).ToList(),
            s.Enrolments.OrderByDescending(e => e.StartDate).Select(e => new EnrolmentView(e.Id, e.AcademicYearId, years.GetValueOrDefault(e.AcademicYearId, "?"), e.CampusId,
                e.YearGroupId, yearGroups.GetValueOrDefault(e.YearGroupId, "?"), e.SectionId, e.SectionId is { } sid ? sections.GetValueOrDefault(sid) : null,
                e.RollNo, e.StartDate, e.EndDate, e.Status, e.Note)).ToList(),
            siblings, docs);
    }

    /// <summary>Direct admission (migration, transfer-in, mid-year joiners) without the admission pipeline.</summary>
    [HttpPost]
    [HasPermission(Permissions.Students.Create)]
    public async Task<ActionResult<StudentDetail>> Create(CreateStudentRequest request, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Students.Create, request.CampusId);
        var year = await db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == request.AcademicYearId, ct) ?? throw new NotFoundException("Academic year", request.AcademicYearId);
        if (request.SectionId is { } sid && !await db.Sections.AnyAsync(s => s.Id == sid && s.CampusId == request.CampusId && s.AcademicYearId == year.Id && s.YearGroupId == request.YearGroupId, ct))
            throw new DomainException("The section does not match the campus, year and class.", "section_mismatch");

        var family = new Family { FamilyCode = await numbers.NextAsync(NumberKeys.Family, ct: ct), Name = request.Guardians.FirstOrDefault()?.Name };
        var student = new Student
        {
            AdmissionNo = await numbers.NextAsync(NumberKeys.AdmissionNo, request.CampusId, year.Id, ct),
            CampusId = request.CampusId, FirstName = request.Profile.FirstName, DateOfBirth = request.Profile.DateOfBirth,
            AdmissionDate = request.AdmissionDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)
        };
        ApplyProfile(student, request.Profile);

        var guardianIds = new List<(Guardian G, GuardianLinkRequest R)>();
        foreach (var g in request.Guardians)
        {
            var phone = StudentImportService.NormalisePhone(g.Phone) ?? g.Phone;
            var guardian = await db.Guardians.FirstOrDefaultAsync(x => x.Phone == phone, ct);
            if (guardian is null)
            {
                guardian = new Guardian { Name = g.Name, Phone = phone, Email = g.Email, NationalId = g.NationalId, Occupation = g.Occupation };
                db.Guardians.Add(guardian);
            }
            guardianIds.Add((guardian, g));
        }
        var existingFamily = guardianIds.Select(x => x.G.FamilyId).FirstOrDefault(f => f is not null);
        if (existingFamily is null) db.Families.Add(family);
        student.FamilyId = existingFamily ?? family.Id;
        foreach (var (g, _) in guardianIds) g.FamilyId ??= student.FamilyId;

        db.Students.Add(student);
        var enrolment = new StudentEnrolment
        {
            StudentId = student.Id, CampusId = request.CampusId, AcademicYearId = year.Id, YearGroupId = request.YearGroupId,
            SectionId = request.SectionId, RollNo = request.SectionId is { } s ? await allocator.NextRollAsync(s, ct) : null, StartDate = student.AdmissionDate
        };
        db.StudentEnrolments.Add(enrolment);
        student.CurrentEnrolmentId = enrolment.Id;
        foreach (var (g, r) in guardianIds)
            db.StudentGuardians.Add(new StudentGuardian
            {
                StudentId = student.Id, GuardianId = g.Id, Relationship = r.Relationship, IsPrimaryContact = r.IsPrimaryContact,
                IsAuthorisedPickup = r.IsAuthorisedPickup, IsEmergencyContact = r.IsPrimaryContact
            });
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = student.Id }, await Get(student.Id, ct));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<StudentDetail> Update(Guid id, StudentProfileRequest request, CancellationToken ct)
    {
        var s = await db.Students.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        currentUser.EnsurePermission(Permissions.Students.Edit, s.CampusId);
        s.FirstName = request.FirstName;
        s.DateOfBirth = request.DateOfBirth;
        ApplyProfile(s, request);
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    [HttpPost("{id:guid}/photo")]
    [HasPermission(Permissions.Students.Edit)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(Guid id, IFormFile file, CancellationToken ct)
    {
        var s = await db.Students.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        currentUser.EnsurePermission(Permissions.Students.Edit, s.CampusId);
        if (!file.ContentType.StartsWith("image/")) throw new DomainException("Upload a JPEG, PNG or WebP image.", "file_type");
        await using var stream = file.OpenReadStream();
        var stored = await storage.SaveAsync(stream, $"students/{id:N}/photo", file.FileName, file.ContentType, ct);
        if (s.PhotoPath is not null) storage.Delete(s.PhotoPath);
        s.PhotoPath = stored.Path;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/photo")]
    [HasPermission(Permissions.Students.View)]
    public async Task<IActionResult> Photo(Guid id, CancellationToken ct)
    {
        var path = await db.Students.AsNoTracking().Where(x => x.Id == id).Select(x => x.PhotoPath).FirstOrDefaultAsync(ct);
        if (path is null) return NotFound();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return File(storage.OpenRead(path), ext == ".png" ? "image/png" : ext == ".webp" ? "image/webp" : "image/jpeg");
    }

    // ---- Guardians (SIS-02)

    /// <summary>Links a guardian to the student: an existing guardian by id, or a new one (matched by phone to avoid duplicates).</summary>
    [HttpPost("{id:guid}/guardians")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<StudentDetail> LinkGuardian(Guid id, GuardianLinkRequest request, CancellationToken ct)
    {
        var s = await db.Students.Include(x => x.Guardians).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        currentUser.EnsurePermission(Permissions.Students.Edit, s.CampusId);

        var phone = StudentImportService.NormalisePhone(request.Phone) ?? request.Phone;
        var guardian = request.GuardianId is { } gid
            ? await db.Guardians.FirstOrDefaultAsync(g => g.Id == gid, ct) ?? throw new NotFoundException("Guardian", gid)
            : await db.Guardians.FirstOrDefaultAsync(g => g.Phone == phone, ct);
        if (guardian is null)
        {
            guardian = new Guardian { Name = request.Name, Phone = phone, Email = request.Email, NationalId = request.NationalId, Occupation = request.Occupation, FamilyId = s.FamilyId };
            db.Guardians.Add(guardian);
        }
        if (s.Guardians.Any(g => g.GuardianId == guardian.Id)) throw new DomainException("This guardian is already linked.", "duplicate", isConflict: true);
        guardian.FamilyId ??= s.FamilyId;

        if (request.IsPrimaryContact) foreach (var g in s.Guardians) g.IsPrimaryContact = false;
        db.StudentGuardians.Add(new StudentGuardian
        {
            StudentId = s.Id, GuardianId = guardian.Id, Relationship = request.Relationship, IsPrimaryContact = request.IsPrimaryContact || s.Guardians.Count == 0,
            IsAuthorisedPickup = request.IsAuthorisedPickup, IsEmergencyContact = request.IsPrimaryContact, CanViewFees = true
        });
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    [HttpPut("{id:guid}/guardians/{guardianId:guid}")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<StudentDetail> UpdateGuardianLink(Guid id, Guid guardianId, GuardianFlagsRequest request, CancellationToken ct)
    {
        var s = await db.Students.Include(x => x.Guardians).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        currentUser.EnsurePermission(Permissions.Students.Edit, s.CampusId);
        var link = s.Guardians.FirstOrDefault(g => g.GuardianId == guardianId) ?? throw new NotFoundException("Guardian link", guardianId);
        if (request.IsPrimaryContact) foreach (var g in s.Guardians) g.IsPrimaryContact = false;
        else if (link.IsPrimaryContact) throw new DomainException("Make another guardian primary first.", "primary_required");
        link.Relationship = request.Relationship;
        link.IsPrimaryContact = request.IsPrimaryContact;
        link.IsAuthorisedPickup = request.IsAuthorisedPickup;
        link.IsEmergencyContact = request.IsEmergencyContact;
        link.CanViewFees = request.CanViewFees;
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    [HttpDelete("{id:guid}/guardians/{guardianId:guid}")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<StudentDetail> UnlinkGuardian(Guid id, Guid guardianId, CancellationToken ct)
    {
        var s = await db.Students.Include(x => x.Guardians).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        currentUser.EnsurePermission(Permissions.Students.Edit, s.CampusId);
        var link = s.Guardians.FirstOrDefault(g => g.GuardianId == guardianId) ?? throw new NotFoundException("Guardian link", guardianId);
        if (link.IsPrimaryContact) throw new DomainException("The primary contact cannot be removed; make another guardian primary first.", "primary_required");
        db.StudentGuardians.Remove(link);
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    /// <summary>Links two students as siblings by merging their families (one family login, combined fee view).</summary>
    [HttpPost("{id:guid}/siblings/{siblingId:guid}")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<StudentDetail> LinkSibling(Guid id, Guid siblingId, CancellationToken ct)
    {
        var s = await db.Students.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        // The sibling may be on a campus the user cannot otherwise see; only its family id is read.
        var sibling = await db.Students.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == siblingId && !x.IsDeleted, ct) ?? throw new NotFoundException("Student", siblingId);
        currentUser.EnsurePermission(Permissions.Students.Edit, s.CampusId);
        if (s.FamilyId == sibling.FamilyId && s.FamilyId is not null) return await Get(id, ct);

        var target = sibling.FamilyId ?? s.FamilyId;
        if (target is null)
        {
            var family = new Family { FamilyCode = await numbers.NextAsync(NumberKeys.Family, ct: ct) };
            db.Families.Add(family);
            target = family.Id;
        }
        var source = s.FamilyId;
        using (db.ElevateToSystem())
        {
            if (source is not null && source != target)
            {
                await db.Students.Where(x => x.FamilyId == source).ForEachAsync(x => x.FamilyId = target, ct);
                await db.Guardians.Where(x => x.FamilyId == source).ForEachAsync(x => x.FamilyId = target, ct);
            }
            s.FamilyId = target;
            sibling.FamilyId = target;
            audit.Add(AuditAction.Custom, nameof(Family), target.ToString(), $"Linked siblings {s.AdmissionNo} and {sibling.AdmissionNo}", s.CampusId);
            await db.SaveChangesAsync(ct);
        }
        return await Get(id, ct);
    }

    // ---- Document vault (SIS-03)

    [HttpPost("{id:guid}/documents")]
    [HasPermission(Permissions.Students.Edit)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<StudentDocumentView> UploadDocument(Guid id, [FromForm] DocumentUploadForm form, CancellationToken ct)
    {
        var s = await db.Students.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        currentUser.EnsurePermission(Permissions.Students.Edit, s.CampusId);
        if (form.IsConfidential) currentUser.EnsurePermission(Permissions.Students.Documents, s.CampusId);

        await using var stream = form.File.OpenReadStream();
        var stored = await storage.SaveAsync(stream, $"students/{id:N}/documents", form.File.FileName, form.File.ContentType, ct);
        var doc = new StudentDocument
        {
            StudentId = id, DocumentType = form.DocumentType, FileName = stored.FileName, StoragePath = stored.Path, ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes, Sha256 = stored.Sha256, ExpiresOn = form.ExpiresOn, IsConfidential = form.IsConfidential
        };
        db.StudentDocuments.Add(doc);
        await db.SaveChangesAsync(ct);
        return new StudentDocumentView(doc.Id, doc.DocumentType, doc.FileName, doc.ContentType, doc.SizeBytes, doc.ExpiresOn, doc.VerificationStatus, doc.IsConfidential, doc.CreatedAtUtc);
    }

    /// <summary>Streams a document after the permission check; files are never served from a public folder. Downloads are audited.</summary>
    [HttpGet("{id:guid}/documents/{documentId:guid}")]
    [HasPermission(Permissions.Students.View)]
    public async Task<IActionResult> DownloadDocument(Guid id, Guid documentId, CancellationToken ct)
    {
        var s = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        var doc = await db.StudentDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.StudentId == id, ct) ?? throw new NotFoundException("Document", documentId);
        if (doc.IsConfidential) currentUser.EnsurePermission(Permissions.Students.Documents, s.CampusId);
        audit.Add(AuditAction.Export, nameof(StudentDocument), doc.Id.ToString(), $"Downloaded {doc.DocumentType} of {s.AdmissionNo}", s.CampusId);
        await db.SaveChangesAsync(ct);
        return File(storage.OpenRead(doc.StoragePath), doc.ContentType, doc.FileName);
    }

    [HttpPost("{id:guid}/documents/{documentId:guid}/verify")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<IActionResult> VerifyDocument(Guid id, Guid documentId, [FromQuery] DocumentVerificationStatus status, CancellationToken ct)
    {
        var s = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        currentUser.EnsurePermission(Permissions.Students.Edit, s.CampusId);
        var doc = await db.StudentDocuments.FirstOrDefaultAsync(d => d.Id == documentId && d.StudentId == id, ct) ?? throw new NotFoundException("Document", documentId);
        doc.VerificationStatus = status;
        doc.VerifiedBy = currentUser.UserId;
        doc.VerifiedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/documents/{documentId:guid}")]
    [HasPermission(Permissions.Students.Documents)]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid documentId, CancellationToken ct)
    {
        var s = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        currentUser.EnsurePermission(Permissions.Students.Documents, s.CampusId);
        var doc = await db.StudentDocuments.FirstOrDefaultAsync(d => d.Id == documentId && d.StudentId == id, ct) ?? throw new NotFoundException("Document", documentId);
        db.StudentDocuments.Remove(doc);
        await db.SaveChangesAsync(ct);
        // Keep the file when an application document still points at it.
        if (!await db.ApplicationDocuments.AnyAsync(d => d.StoragePath == doc.StoragePath, ct)) storage.Delete(doc.StoragePath);
        return NoContent();
    }

    /// <summary>Documents expiring within the given number of days (e.g. passports, visas, vaccination cards).</summary>
    [HttpGet("documents/expiring")]
    [HasPermission(Permissions.Students.View)]
    public async Task<List<ExpiringDocument>> ExpiringDocuments([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var limit = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(days);
        return await db.StudentDocuments.AsNoTracking()
            .Where(d => d.ExpiresOn != null && d.ExpiresOn <= limit)
            .Join(db.Students, d => d.StudentId, s => s.Id, (d, s) => new { d, s })
            .OrderBy(x => x.d.ExpiresOn)
            .Select(x => new ExpiringDocument(x.s.Id, x.s.AdmissionNo, x.s.FirstName + " " + (x.s.LastName ?? ""), x.d.Id, x.d.DocumentType, x.d.ExpiresOn!.Value))
            .ToListAsync(ct);
    }

    // ---- Transfers (SIS-04)

    /// <summary>Moves a student to another section or campus mid-year; the old enrolment is closed so history is kept.</summary>
    [HttpPost("{id:guid}/transfer")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<StudentDetail> Transfer(Guid id, TransferRequest request, CancellationToken ct)
    {
        var s = await db.Students.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        var current = await db.StudentEnrolments.FirstOrDefaultAsync(e => e.Id == s.CurrentEnrolmentId && e.Status == EnrolmentStatus.Active, ct)
                      ?? throw new DomainException("The student has no active enrolment.", "no_enrolment");
        // Target section may be on a campus the user is moving the student into; it must still be visible to them.
        var target = await db.Sections.FirstOrDefaultAsync(x => x.Id == request.TargetSectionId, ct) ?? throw new NotFoundException("Section", request.TargetSectionId);
        currentUser.EnsurePermission(Permissions.Students.Edit, s.CampusId);
        currentUser.EnsurePermission(Permissions.Students.Edit, target.CampusId);
        if (target.AcademicYearId != current.AcademicYearId) throw new DomainException("The target section must be in the same academic year; use promotion for year changes.", "year_mismatch");
        if (target.Id == current.SectionId) throw new DomainException("The student is already in this section.", "same_section");

        current.Status = EnrolmentStatus.Transferred;
        current.EndDate = request.EffectiveDate.AddDays(-1);
        current.Note = request.Reason;
        var next = new StudentEnrolment
        {
            StudentId = s.Id, CampusId = target.CampusId, AcademicYearId = target.AcademicYearId, YearGroupId = target.YearGroupId,
            SectionId = target.Id, RollNo = await allocator.NextRollAsync(target.Id, ct), StartDate = request.EffectiveDate, Note = request.Reason
        };
        db.StudentEnrolments.Add(next);
        s.CurrentEnrolmentId = next.Id;
        s.CampusId = target.CampusId;
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    private static void ApplyProfile(Student s, StudentProfileRequest r)
    {
        s.LastName = r.LastName;
        s.NameBn = r.NameBn;
        s.Gender = r.Gender;
        s.BloodGroup = r.BloodGroup;
        s.Religion = r.Religion;
        s.Nationality = r.Nationality;
        s.BirthCertificateNo = r.BirthCertificateNo;
        s.NationalId = r.NationalId;
        s.PresentAddress = r.PresentAddress;
        s.PermanentAddress = r.PermanentAddress;
        s.PreviousSchool = r.PreviousSchool;
        s.Languages = r.Languages;
        s.SpecialNeeds = r.SpecialNeeds;
        s.Email = r.Email;
        s.Phone = r.Phone;
        s.HouseId = r.HouseId;
        s.RfidNo = r.RfidNo;
    }
}

internal static class StudentQueries
{
    // Member-init (not constructor) projection so EF can still filter and sort on these properties.
    public sealed class Row
    {
        public Guid Id { get; init; }
        public string AdmissionNo { get; init; } = "";
        public string FirstName { get; init; } = "";
        public string? LastName { get; init; }
        public string? NameBn { get; init; }
        public string? Gender { get; init; }
        public DateOnly DateOfBirth { get; init; }
        public Guid CampusId { get; init; }
        public Guid? YearGroupId { get; init; }
        public string? ClassName { get; init; }
        public int ClassSequence { get; init; }
        public Guid? SectionId { get; init; }
        public string? Section { get; init; }
        public int? RollNo { get; init; }
        public StudentStatus Status { get; init; }
        public string? GuardianPhone { get; init; }
        public string? GuardianName { get; init; }
        public string? GuardianEmail { get; init; }
        public string? BloodGroup { get; init; }
        public Guid? HouseId { get; init; }
    }

    public static IQueryable<Row> Filter(AppDbContext db, StudentQuery q)
    {
        var rows =
            from s in db.Students.AsNoTracking()
            join e in db.StudentEnrolments on s.CurrentEnrolmentId equals e.Id into es
            from e in es.DefaultIfEmpty()
            join y in db.YearGroups on e.YearGroupId equals y.Id into ys
            from y in ys.DefaultIfEmpty()
            join sec in db.Sections on e.SectionId equals sec.Id into secs
            from sec in secs.DefaultIfEmpty()
            let primary = s.Guardians.Where(g => g.IsPrimaryContact).Select(g => g.Guardian).FirstOrDefault()
            select new Row
            {
                Id = s.Id, AdmissionNo = s.AdmissionNo, FirstName = s.FirstName, LastName = s.LastName, NameBn = s.NameBn, Gender = s.Gender,
                DateOfBirth = s.DateOfBirth, CampusId = s.CampusId,
                YearGroupId = e != null ? e.YearGroupId : null, ClassName = y != null ? y.Name : null, ClassSequence = y != null ? y.Sequence : int.MaxValue,
                SectionId = e != null ? e.SectionId : null, Section = sec != null ? sec.Name : null, RollNo = e != null ? e.RollNo : null, Status = s.Status,
                GuardianPhone = primary != null ? primary.Phone : null, GuardianName = primary != null ? primary.Name : null,
                GuardianEmail = primary != null ? primary.Email : null, BloodGroup = s.BloodGroup, HouseId = s.HouseId
            };

        if (q.CampusId is not null) rows = rows.Where(r => r.CampusId == q.CampusId);
        if (q.YearGroupId is not null) rows = rows.Where(r => r.YearGroupId == q.YearGroupId);
        if (q.SectionId is not null) rows = rows.Where(r => r.SectionId == q.SectionId);
        if (q.HouseId is not null) rows = rows.Where(r => r.HouseId == q.HouseId);
        if (q.Gender is not null) rows = rows.Where(r => r.Gender == q.Gender);
        rows = rows.Where(r => r.Status == (q.Status ?? StudentStatus.Active));
        if (!string.IsNullOrWhiteSpace(q.Search))
            rows = rows.Where(r => r.AdmissionNo == q.Search || r.FirstName.Contains(q.Search) || (r.LastName != null && r.LastName.Contains(q.Search)) ||
                                   (r.NameBn != null && r.NameBn.Contains(q.Search)) || (r.GuardianPhone != null && r.GuardianPhone.Contains(q.Search)));
        return rows;
    }
}

public record StudentQuery(Guid? CampusId, Guid? YearGroupId, Guid? SectionId, Guid? HouseId, string? Gender, StudentStatus? Status, string? Search);
public record StudentListItem(Guid Id, string AdmissionNo, string Name, string? NameBn, string? Gender, DateOnly DateOfBirth, Guid CampusId, Guid? YearGroupId,
    string? ClassName, Guid? SectionId, string? Section, int? RollNo, StudentStatus Status, string? GuardianPhone);
public record StudentGuardianView(Guid GuardianId, string Name, string? NameBn, string Relationship, string Phone, string? Email, string? Occupation,
    bool IsPrimaryContact, bool IsAuthorisedPickup, bool IsEmergencyContact, bool CanViewFees, bool HasPortalAccount);
public record EnrolmentView(Guid Id, Guid AcademicYearId, string AcademicYear, Guid CampusId, Guid YearGroupId, string ClassName, Guid? SectionId, string? Section,
    int? RollNo, DateOnly StartDate, DateOnly? EndDate, EnrolmentStatus Status, string? Note);
public record SiblingView(Guid Id, string AdmissionNo, string Name, Guid CampusId, StudentStatus Status);
public record StudentDocumentView(Guid Id, string DocumentType, string FileName, string ContentType, long SizeBytes, DateOnly? ExpiresOn,
    DocumentVerificationStatus VerificationStatus, bool IsConfidential, DateTime UploadedAtUtc);
public record StudentDetail(Guid Id, string AdmissionNo, Guid CampusId, Guid? FamilyId, Guid? ApplicationId, string FirstName, string? LastName, string? NameBn,
    DateOnly DateOfBirth, string? Gender, string? BloodGroup, string? Religion, string? Nationality, string? BirthCertificateNo, string? NationalId, bool HasPhoto,
    string? PresentAddress, string? PermanentAddress, string? PreviousSchool, string? Languages, string? SpecialNeeds, string? Email, string? Phone, Guid? HouseId,
    string? RfidNo, DateOnly AdmissionDate, StudentStatus Status, DateOnly? LeftOn, Guid? CurrentEnrolmentId, IReadOnlyList<StudentGuardianView> Guardians,
    IReadOnlyList<EnrolmentView> Enrolments, IReadOnlyList<SiblingView> Siblings, IReadOnlyList<StudentDocumentView> Documents);
public record StudentProfileRequest(string FirstName, string? LastName, string? NameBn, DateOnly DateOfBirth, string? Gender, string? BloodGroup, string? Religion,
    string? Nationality, string? BirthCertificateNo, string? NationalId, string? PresentAddress, string? PermanentAddress, string? PreviousSchool, string? Languages,
    string? SpecialNeeds, string? Email, string? Phone, Guid? HouseId, string? RfidNo);
public record GuardianLinkRequest(Guid? GuardianId, string Name, string Relationship, string Phone, string? Email, string? NationalId, string? Occupation,
    bool IsPrimaryContact, bool IsAuthorisedPickup = true);
public record GuardianFlagsRequest(string Relationship, bool IsPrimaryContact, bool IsAuthorisedPickup, bool IsEmergencyContact, bool CanViewFees);
public record CreateStudentRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, Guid? SectionId, DateOnly? AdmissionDate, StudentProfileRequest Profile, List<GuardianLinkRequest> Guardians);
public record TransferRequest(Guid TargetSectionId, DateOnly EffectiveDate, string Reason);
public record ExpiringDocument(Guid StudentId, string AdmissionNo, string StudentName, Guid DocumentId, string DocumentType, DateOnly ExpiresOn);

public sealed class DocumentUploadForm
{
    public required string DocumentType { get; init; }
    public required IFormFile File { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public bool IsConfidential { get; init; }
}

public sealed class StudentProfileRequestValidator : AbstractValidator<StudentProfileRequest>
{
    public StudentProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).MaximumLength(100);
        RuleFor(x => x.Gender).Must(g => g is null or "Male" or "Female").WithMessage("Gender must be Male or Female.");
        RuleFor(x => x.BloodGroup).Must(b => b is null or "A+" or "A-" or "B+" or "B-" or "AB+" or "AB-" or "O+" or "O-");
        RuleFor(x => x.BirthCertificateNo).Matches(@"^\d{17}$").When(x => !string.IsNullOrEmpty(x.BirthCertificateNo));
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
    }
}

public sealed class GuardianLinkRequestValidator : AbstractValidator<GuardianLinkRequest>
{
    public GuardianLinkRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().When(x => x.GuardianId is null);
        RuleFor(x => x.Relationship).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Phone).Must(p => StudentImportService.NormalisePhone(p) is not null).When(x => x.GuardianId is null)
            .WithMessage("Enter a valid Bangladeshi mobile number.");
    }
}

public sealed class CreateStudentRequestValidator : AbstractValidator<CreateStudentRequest>
{
    public CreateStudentRequestValidator()
    {
        RuleFor(x => x.Profile).NotNull().SetValidator(new StudentProfileRequestValidator());
        RuleFor(x => x.Guardians).NotEmpty().Must(g => g.Count(x => x.IsPrimaryContact) == 1).WithMessage("Exactly one guardian must be the primary contact.");
        RuleForEach(x => x.Guardians).SetValidator(new GuardianLinkRequestValidator());
    }
}
