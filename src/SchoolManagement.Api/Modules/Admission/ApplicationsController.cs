using SchoolManagement.Api.Modules.Admission.Services;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Infrastructure.Storage;

namespace SchoolManagement.Api.Modules.Admission;

/// <summary>ADM-03 applications, ADM-04 interviews, ADM-05/06 offer &amp; waiting list for one applicant, ADM-08 enrolment.</summary>
[ApiController]
[Route("api/admission/applications")]
[Tags("Admission: Applications")]
public sealed class ApplicationsController(
    AppDbContext db,
    ApplicationService applications,
    SelectionService selection,
    EnrolmentService enrolment,
    IFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Admission.View)]
    public async Task<PagedResult<ApplicationListItem>> List([FromQuery] ApplicationQuery query, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var q = db.Applications.AsNoTracking();
        if (query.CampusId is not null) q = q.Where(a => a.CampusId == query.CampusId);
        if (query.AcademicYearId is not null) q = q.Where(a => a.AcademicYearId == query.AcademicYearId);
        if (query.YearGroupId is not null) q = q.Where(a => a.YearGroupId == query.YearGroupId);
        if (query.Status is not null) q = q.Where(a => a.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(a => a.ApplicationNo == query.Search || a.FirstName.Contains(query.Search) || (a.LastName != null && a.LastName.Contains(query.Search)) ||
                             a.Guardians.Any(g => g.Phone.Contains(query.Search)));
        return await q.OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new ApplicationListItem(a.Id, a.ApplicationNo, a.CampusId, a.AcademicYearId, a.YearGroupId, a.FirstName + " " + (a.LastName ?? ""),
                a.DateOfBirth, a.Gender, a.Status, a.TestScore, a.MeritScore, a.MeritRank, a.ApplicationFeePaid, a.IsOnline, a.SubmittedAtUtc,
                a.Guardians.Where(g => g.IsPrimaryContact).Select(g => g.Phone).FirstOrDefault()))
            .ToPagedAsync(page, ct);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<ApplicationDetail> Get(Guid id, CancellationToken ct)
    {
        var a = await db.Applications.AsNoTracking().Include(x => x.Guardians).Include(x => x.Documents).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Application", id);
        return await DetailAsync(a, db, ct);
    }

    [HttpPost]
    [HasPermission(Permissions.Admission.Create)]
    public async Task<ActionResult<ApplicationDetail>> Create(ApplicationRequest request, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Admission.Create, request.CampusId);
        var (app, _) = await applications.CreateAsync(request, online: false, ct);
        return CreatedAtAction(nameof(Get), new { id = app.Id }, await DetailAsync(app, db, ct));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<ApplicationDetail> Update(Guid id, ApplicationRequest request, CancellationToken ct)
    {
        var app = await Load(id, ct);
        currentUser.EnsurePermission(Permissions.Admission.Edit, app.CampusId);
        currentUser.EnsurePermission(Permissions.Admission.Edit, request.CampusId);
        await applications.UpdateAsync(app, request, ct);
        return await DetailAsync(app, db, ct);
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<ApplicationDetail> Submit(Guid id, CancellationToken ct)
    {
        var app = await Load(id, ct);
        await applications.SubmitAsync(app, ct);
        return await DetailAsync(app, db, ct);
    }

    [HttpPost("{id:guid}/documents")]
    [HasPermission(Permissions.Admission.Edit)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<DocumentView> UploadDocument(Guid id, [FromForm] string documentType, IFormFile file, CancellationToken ct)
    {
        var app = await Load(id, ct);
        var doc = await applications.AddDocumentAsync(app, file, documentType, ct);
        return new DocumentView(doc.Id, doc.DocumentType, doc.FileName, doc.ContentType, doc.SizeBytes, doc.IsVerified, doc.CreatedAtUtc);
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<IActionResult> DownloadDocument(Guid id, Guid documentId, CancellationToken ct)
    {
        await Load(id, ct);
        var doc = await db.ApplicationDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && d.ApplicationId == id, ct)
                  ?? throw new NotFoundException("Document", documentId);
        return File(storage.OpenRead(doc.StoragePath), doc.ContentType, doc.FileName);
    }

    [HttpPost("{id:guid}/documents/{documentId:guid}/verify")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<IActionResult> VerifyDocument(Guid id, Guid documentId, [FromQuery] bool verified, CancellationToken ct)
    {
        await Load(id, ct);
        var doc = await db.ApplicationDocuments.FirstOrDefaultAsync(d => d.Id == documentId && d.ApplicationId == id, ct) ?? throw new NotFoundException("Document", documentId);
        doc.IsVerified = verified;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Records the application fee paid at the counter (online gateway payments arrive with FEE-04 in release 2).</summary>
    [HttpPost("{id:guid}/application-fee")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<ApplicationDetail> RecordApplicationFee(Guid id, ApplicationFeeRequest request, CancellationToken ct)
    {
        var app = await Load(id, ct);
        if (app.ApplicationFeePaid) throw new DomainException("The application fee is already recorded.", "already_paid", isConflict: true);
        app.ApplicationFee = request.Amount;
        app.ApplicationFeePaid = true;
        app.ApplicationFeeReference = $"{request.Channel}:{request.Reference}";
        if (app.Status == ApplicationStatus.Submitted) app.Status = ApplicationStatus.UnderReview;
        await db.SaveChangesAsync(ct);
        return await DetailAsync(app, db, ct);
    }

    [HttpPost("{id:guid}/reject")]
    [HasPermission(Permissions.Admission.Approve)]
    public async Task<ApplicationDetail> Reject(Guid id, ReasonRequest request, CancellationToken ct)
    {
        var app = await Load(id, ct);
        currentUser.EnsurePermission(Permissions.Admission.Approve, app.CampusId);
        if (app.Status is ApplicationStatus.Enrolled or ApplicationStatus.Accepted)
            throw new DomainException($"Cannot reject an application that is {app.Status}.", "invalid_status", isConflict: true);
        var freesSeat = app.Status == ApplicationStatus.Offered;
        await CloseOpenOffersAndWaitlistAsync(app, ct);
        app.Status = ApplicationStatus.Rejected;
        app.DecisionNote = request.Reason;
        await selection.SetEnquiryStageAsync(app, EnquiryStage.Lost, ct);
        await db.SaveChangesAsync(ct);
        if (freesSeat) await selection.PromoteWaitlistAsync(new ClassKey(app.CampusId, app.AcademicYearId, app.YearGroupId), ct);
        return await DetailAsync(app, db, ct);
    }

    [HttpPost("{id:guid}/withdraw")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<ApplicationDetail> Withdraw(Guid id, ReasonRequest request, CancellationToken ct)
    {
        var app = await Load(id, ct);
        if (app.Status == ApplicationStatus.Enrolled)
            throw new DomainException("The applicant is already enrolled; use the student withdrawal process.", "invalid_status", isConflict: true);
        var freesSeat = app.Status is ApplicationStatus.Offered or ApplicationStatus.Accepted;
        await CloseOpenOffersAndWaitlistAsync(app, ct);
        await db.AdmissionInvoices.Where(i => i.ApplicationId == id && i.Status == AdmissionInvoiceStatus.Unpaid)
            .ForEachAsync(i => i.Status = AdmissionInvoiceStatus.Cancelled, ct);
        app.Status = ApplicationStatus.Withdrawn;
        app.DecisionNote = request.Reason;
        await selection.SetEnquiryStageAsync(app, EnquiryStage.Lost, ct);
        await db.SaveChangesAsync(ct);
        if (freesSeat) await selection.PromoteWaitlistAsync(new ClassKey(app.CampusId, app.AcademicYearId, app.YearGroupId), ct);
        return await DetailAsync(app, db, ct);
    }

    [HttpPost("{id:guid}/interviews")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<InterviewView> ScheduleInterview(Guid id, InterviewRequest request, CancellationToken ct)
    {
        var app = await Load(id, ct);
        var interview = new Interview
        {
            ApplicationId = app.Id, ScheduledAtUtc = request.ScheduledAtUtc, Location = request.Location,
            InterviewerName = request.InterviewerName, InterviewerUserId = request.InterviewerUserId
        };
        db.Interviews.Add(interview);
        await db.SaveChangesAsync(ct);
        return ToView(interview);
    }

    [HttpPut("interviews/{interviewId:guid}")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<InterviewView> RecordInterview(Guid interviewId, InterviewResultRequest request, CancellationToken ct)
    {
        var interview = await db.Interviews.FirstOrDefaultAsync(i => i.Id == interviewId, ct) ?? throw new NotFoundException("Interview", interviewId);
        var app = await Load(interview.ApplicationId, ct);
        interview.Score = request.Score;
        interview.Notes = request.Notes;
        interview.IsCompleted = true;
        app.InterviewScore = request.Score;
        await db.SaveChangesAsync(ct);
        return ToView(interview);
    }

    /// <summary>Offers a seat to this applicant directly (outside the merit-list batch).</summary>
    [HttpPost("{id:guid}/offer")]
    [HasPermission(Permissions.Admission.Approve)]
    public async Task<OfferView> MakeOffer(Guid id, OfferOptions options, CancellationToken ct)
    {
        var app = await Load(id, ct);
        currentUser.EnsurePermission(Permissions.Admission.Approve, app.CampusId);
        var seat = await selection.SeatAsync(new ClassKey(app.CampusId, app.AcademicYearId, app.YearGroupId), null, ct);
        if (seat.Available <= 0 && app.Status != ApplicationStatus.Waitlisted)
            throw new DomainException("No seats are available in this class; add the applicant to the waiting list.", "no_seats", isConflict: true);
        var offer = await selection.CreateOfferAsync(app, options, ct);
        await db.SaveChangesAsync(ct);
        return OffersController.ToView(offer, app);
    }

    [HttpPost("{id:guid}/waitlist")]
    [HasPermission(Permissions.Admission.Approve)]
    public async Task<ApplicationDetail> Waitlist(Guid id, CancellationToken ct)
    {
        var app = await Load(id, ct);
        await selection.AddToWaitlistAsync(app, ct);
        await db.SaveChangesAsync(ct);
        return await DetailAsync(app, db, ct);
    }

    /// <summary>ADM-08: converts an accepted (and, if configured, paid) applicant into a student in one step.</summary>
    [HttpPost("{id:guid}/enrol")]
    [HasPermission(Permissions.Students.Create)]
    public Task<EnrolResult> Enrol(Guid id, EnrolRequest request, CancellationToken ct) => enrolment.EnrolAsync(id, request, ct);

    private async Task CloseOpenOffersAndWaitlistAsync(Application app, CancellationToken ct)
    {
        await db.Offers.Where(o => o.ApplicationId == app.Id && o.Status == OfferStatus.Pending)
            .ForEachAsync(o => { o.Status = OfferStatus.Revoked; o.RespondedAtUtc = clock.GetUtcNow().UtcDateTime; }, ct);
        await db.WaitlistEntries.Where(w => w.ApplicationId == app.Id && w.IsActive).ForEachAsync(w => w.IsActive = false, ct);
    }

    private async Task<Application> Load(Guid id, CancellationToken ct) =>
        await db.Applications.Include(a => a.Guardians).Include(a => a.Documents).FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new NotFoundException("Application", id);

    private static InterviewView ToView(Interview i) => new(i.Id, i.ScheduledAtUtc, i.Location, i.InterviewerName, i.Score, i.Notes, i.IsCompleted);

    internal static async Task<ApplicationDetail> DetailAsync(Application a, AppDbContext db, CancellationToken ct)
    {
        var tests = await db.TestCandidates.AsNoTracking().Where(c => c.ApplicationId == a.Id)
            .Join(db.TestSessions, c => c.TestSessionId, s => s.Id, (c, s) => new TestResultView(s.Id, s.Name, s.StartsAtUtc, s.Room, c.AdmitCardNo, c.SeatNo, c.Attended, c.Score, s.MaxScore))
            .ToListAsync(ct);
        var interviews = await db.Interviews.AsNoTracking().Where(i => i.ApplicationId == a.Id).OrderBy(i => i.ScheduledAtUtc)
            .Select(i => new InterviewView(i.Id, i.ScheduledAtUtc, i.Location, i.InterviewerName, i.Score, i.Notes, i.IsCompleted)).ToListAsync(ct);
        var offers = await db.Offers.AsNoTracking().Where(o => o.ApplicationId == a.Id).OrderByDescending(o => o.IssuedAtUtc).ToListAsync(ct);
        var invoices = await db.AdmissionInvoices.AsNoTracking().Include(i => i.Lines).Where(i => i.ApplicationId == a.Id).ToListAsync(ct);
        var waitlist = await db.WaitlistEntries.AsNoTracking().Where(w => w.ApplicationId == a.Id && w.IsActive).Select(w => (int?)w.Position).FirstOrDefaultAsync(ct);

        return new ApplicationDetail(a.Id, a.ApplicationNo, a.CampusId, a.AcademicYearId, a.YearGroupId, a.EnquiryId, a.Status, a.IsOnline,
            a.FirstName, a.LastName, a.NameBn, a.DateOfBirth, a.Gender, a.Religion, a.Nationality, a.BloodGroup, a.BirthCertificateNo,
            a.PreviousSchool, a.PreviousClass, a.PresentAddress, a.PermanentAddress, a.SpecialNeeds, a.HasSiblingInSchool, a.SiblingStudentId, a.IsStaffWard,
            a.ApplicationFee, a.ApplicationFeePaid, a.TestScore, a.InterviewScore, a.MeritScore, a.MeritRank, waitlist, a.DecisionNote, a.SubmittedAtUtc, a.StudentId,
            a.Guardians.Select(g => new ApplicationGuardianRequest(g.Relationship, g.Name, g.Phone, g.Email, g.NationalId, g.Occupation, g.Organisation, g.IncomeBand, g.IsPrimaryContact)).ToList(),
            a.Documents.Select(d => new DocumentView(d.Id, d.DocumentType, d.FileName, d.ContentType, d.SizeBytes, d.IsVerified, d.CreatedAtUtc)).ToList(),
            tests, interviews, offers.Select(o => OffersController.ToView(o, a)).ToList(), invoices.Select(InvoicesController.ToView).ToList());
    }
}

public record ApplicationQuery(Guid? CampusId, Guid? AcademicYearId, Guid? YearGroupId, ApplicationStatus? Status, string? Search);
public record ApplicationListItem(Guid Id, string ApplicationNo, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string Name, DateOnly DateOfBirth, string? Gender,
    ApplicationStatus Status, decimal? TestScore, decimal? MeritScore, int? MeritRank, bool ApplicationFeePaid, bool IsOnline, DateTime? SubmittedAtUtc, string? GuardianPhone);
public record DocumentView(Guid Id, string DocumentType, string FileName, string ContentType, long SizeBytes, bool IsVerified, DateTime UploadedAtUtc);
public record TestResultView(Guid TestSessionId, string SessionName, DateTime StartsAtUtc, string? Room, string AdmitCardNo, string? SeatNo, bool? Attended, decimal? Score, decimal MaxScore);
public record InterviewView(Guid Id, DateTime ScheduledAtUtc, string? Location, string? InterviewerName, decimal? Score, string? Notes, bool IsCompleted);
public record ApplicationDetail(Guid Id, string ApplicationNo, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, Guid? EnquiryId, ApplicationStatus Status, bool IsOnline,
    string FirstName, string? LastName, string? NameBn, DateOnly DateOfBirth, string? Gender, string? Religion, string? Nationality, string? BloodGroup, string? BirthCertificateNo,
    string? PreviousSchool, string? PreviousClass, string? PresentAddress, string? PermanentAddress, string? SpecialNeeds, bool HasSiblingInSchool, Guid? SiblingStudentId, bool IsStaffWard,
    decimal ApplicationFee, bool ApplicationFeePaid, decimal? TestScore, decimal? InterviewScore, decimal? MeritScore, int? MeritRank, int? WaitlistPosition, string? DecisionNote,
    DateTime? SubmittedAtUtc, Guid? StudentId, IReadOnlyList<ApplicationGuardianRequest> Guardians, IReadOnlyList<DocumentView> Documents,
    IReadOnlyList<TestResultView> Tests, IReadOnlyList<InterviewView> Interviews, IReadOnlyList<OfferView> Offers, IReadOnlyList<InvoiceView> Invoices);
public record ApplicationFeeRequest(decimal Amount, string Channel, string Reference);
public record ReasonRequest(string Reason);
public record InterviewRequest(DateTime ScheduledAtUtc, string? Location, string? InterviewerName, Guid? InterviewerUserId);
public record InterviewResultRequest(decimal Score, string? Notes);

public sealed class ApplicationFeeRequestValidator : AbstractValidator<ApplicationFeeRequest>
{
    public ApplicationFeeRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Channel).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(64);
    }
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public sealed class InterviewResultRequestValidator : AbstractValidator<InterviewResultRequest>
{
    public InterviewResultRequestValidator() => RuleFor(x => x.Score).InclusiveBetween(0, 100);
}
