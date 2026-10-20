using SchoolManagement.Api.Modules.Admission.Services;
using SchoolManagement.Api.Modules.Students.Services;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Notifications;
using SchoolManagement.Infrastructure.Numbering;

namespace SchoolManagement.Api.Modules.Admission;

/// <summary>ADM-01 enquiry inbox and ADM-02 lead pipeline with tasks, call notes and conversion analytics.</summary>
[ApiController]
[Route("api/admission/enquiries")]
[Tags("Admission: Enquiries")]
public sealed class EnquiriesController(
    AppDbContext db,
    EnquiryIntake intake,
    ICurrentUser currentUser,
    TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Admission.View)]
    public async Task<PagedResult<EnquiryListItem>> List([FromQuery] EnquiryQuery query, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var q = db.Enquiries.AsNoTracking();
        if (query.CampusId is not null) q = q.Where(e => e.CampusId == query.CampusId);
        if (query.Stage is not null) q = q.Where(e => e.Stage == query.Stage);
        if (query.Source is not null) q = q.Where(e => e.Source == query.Source);
        if (query.AssignedToUserId is not null) q = q.Where(e => e.AssignedToUserId == query.AssignedToUserId);
        if (query.Unassigned == true) q = q.Where(e => e.AssignedToUserId == null);
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(e => e.EnquiryNo == query.Search || e.ChildName.Contains(query.Search) || e.GuardianName.Contains(query.Search) || e.GuardianPhone.Contains(query.Search));
        return await q.OrderByDescending(e => e.CreatedAtUtc)
            .Select(e => new EnquiryListItem(e.Id, e.EnquiryNo, e.CampusId, e.Source, e.ChildName, e.GuardianName, e.GuardianPhone, e.YearGroupId,
                e.Stage, e.AssignedToUserId, e.CreatedAtUtc,
                e.Activities.Where(a => a.Type == EnquiryActivityType.Task && !a.IsCompleted).Min(a => a.DueAtUtc)))
            .ToPagedAsync(page, ct);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<EnquiryDetail> Get(Guid id, CancellationToken ct)
    {
        var e = await db.Enquiries.AsNoTracking().Include(x => x.Activities).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Enquiry", id);
        return ToDetail(e);
    }

    /// <summary>Phone, walk-in or social-media enquiry captured by staff. Website forms use the public endpoint.</summary>
    [HttpPost]
    [HasPermission(Permissions.Admission.Create)]
    public async Task<EnquiryDetail> Create(EnquiryRequest request, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Admission.Create, request.CampusId);
        var e = await intake.CreateAsync(request, ct);
        return ToDetail(e);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<EnquiryDetail> Update(Guid id, EnquiryRequest request, CancellationToken ct)
    {
        var e = await Load(id, ct);
        e.CampusId = request.CampusId;
        e.Source = request.Source;
        e.CampaignCode = request.CampaignCode;
        e.AcademicYearId = request.AcademicYearId;
        e.YearGroupId = request.YearGroupId;
        e.ChildName = request.ChildName;
        e.ChildDateOfBirth = request.ChildDateOfBirth;
        e.GuardianName = request.GuardianName;
        e.GuardianPhone = StudentImportService.NormalisePhone(request.GuardianPhone) ?? request.GuardianPhone;
        e.GuardianEmail = request.GuardianEmail;
        e.Message = request.Message;
        await db.SaveChangesAsync(ct);
        return ToDetail(e);
    }

    /// <summary>Moves the enquiry along the pipeline (Enquiry → Campus visit → Applied → Test/Interview → Offered → Admitted / Lost).</summary>
    [HttpPost("{id:guid}/stage")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<EnquiryDetail> ChangeStage(Guid id, StageChangeRequest request, CancellationToken ct)
    {
        var e = await Load(id, ct);
        if (request.Stage == EnquiryStage.Lost && string.IsNullOrWhiteSpace(request.LostReason))
            throw new DomainException("A reason is required when marking an enquiry as lost.", "reason_required");
        var from = e.Stage;
        e.Stage = request.Stage;
        e.LostReason = request.Stage == EnquiryStage.Lost ? request.LostReason : null;
        e.Activities.Add(new EnquiryActivity { EnquiryId = e.Id, Type = EnquiryActivityType.StageChange, Text = $"{from} → {request.Stage}. {request.Note}".Trim() });
        await db.SaveChangesAsync(ct);
        return ToDetail(e);
    }

    [HttpPost("{id:guid}/assign")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<EnquiryDetail> Assign(Guid id, AssignRequest request, CancellationToken ct)
    {
        var e = await Load(id, ct);
        if (request.UserId is { } uid && !await db.Users.AnyAsync(u => u.Id == uid && u.IsActive, ct)) throw new NotFoundException("User", uid);
        e.AssignedToUserId = request.UserId;
        await db.SaveChangesAsync(ct);
        return ToDetail(e);
    }

    /// <summary>Adds a call note, task (with due date reminder) or other activity.</summary>
    [HttpPost("{id:guid}/activities")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<EnquiryDetail> AddActivity(Guid id, ActivityRequest request, CancellationToken ct)
    {
        var e = await Load(id, ct);
        e.Activities.Add(new EnquiryActivity
        {
            EnquiryId = e.Id, Type = request.Type, Text = request.Text, DueAtUtc = request.DueAtUtc,
            AssignedToUserId = request.AssignedToUserId ?? e.AssignedToUserId ?? currentUser.UserId
        });
        if (request.Type == EnquiryActivityType.Call && e.Stage == EnquiryStage.Enquiry && request.MarkCampusVisit)
            e.Stage = EnquiryStage.CampusVisit;
        await db.SaveChangesAsync(ct);
        return ToDetail(e);
    }

    [HttpPost("activities/{activityId:guid}/complete")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<IActionResult> CompleteActivity(Guid activityId, CancellationToken ct)
    {
        var a = await db.EnquiryActivities.FirstOrDefaultAsync(x => x.Id == activityId, ct) ?? throw new NotFoundException("Activity", activityId);
        // Activities are not campus-scoped themselves; check through the parent enquiry.
        if (!await db.Enquiries.AnyAsync(e => e.Id == a.EnquiryId, ct)) throw new NotFoundException("Activity", activityId);
        a.IsCompleted = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Open follow-up tasks for the current counsellor, overdue first.</summary>
    [HttpGet("my-tasks")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<List<TaskItem>> MyTasks(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.EnquiryActivities.AsNoTracking()
            .Where(a => a.Type == EnquiryActivityType.Task && !a.IsCompleted && a.AssignedToUserId == currentUser.UserId)
            .Join(db.Enquiries, a => a.EnquiryId, e => e.Id, (a, e) => new { a, e })
            .OrderBy(x => x.a.DueAtUtc)
            .Select(x => new TaskItem(x.a.Id, x.e.Id, x.e.EnquiryNo, x.e.ChildName, x.e.GuardianPhone, x.a.Text, x.a.DueAtUtc, x.a.DueAtUtc < now))
            .ToListAsync(ct);
    }

    /// <summary>Starts a draft application pre-filled from the enquiry.</summary>
    [HttpPost("{id:guid}/convert")]
    [HasPermission(Permissions.Admission.Create)]
    public async Task<ActionResult<Guid>> Convert(Guid id, ConvertRequest request, CancellationToken ct)
    {
        var e = await Load(id, ct);
        if (e.ApplicationId is not null) throw new DomainException("This enquiry already has an application.", "already_converted", isConflict: true);
        var app = await intake.ApplicationFromEnquiryAsync(e, request.AcademicYearId ?? e.AcademicYearId, request.YearGroupId ?? e.YearGroupId, ct);
        return CreatedAtAction(nameof(ApplicationsController.Get), "Applications", new { id = app.Id }, app.Id);
    }

    /// <summary>Conversion analytics per source and per counsellor (ADM-02).</summary>
    [HttpGet("analytics")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<EnquiryAnalytics> Analytics([FromQuery] Guid? campusId, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var q = db.Enquiries.AsNoTracking();
        if (campusId is not null) q = q.Where(e => e.CampusId == campusId);
        if (fromUtc is not null) q = q.Where(e => e.CreatedAtUtc >= fromUtc);
        if (toUtc is not null) q = q.Where(e => e.CreatedAtUtc <= toUtc);

        var rows = await q.Select(e => new { e.Source, e.Stage, e.AssignedToUserId }).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.FullName }).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        static FunnelRow Row(string key, IEnumerable<EnquiryStage> stages)
        {
            var list = stages.ToList();
            var applied = list.Count(s => s is >= EnquiryStage.Applied and <= EnquiryStage.Admitted);
            var admitted = list.Count(s => s == EnquiryStage.Admitted);
            var lost = list.Count(s => s == EnquiryStage.Lost);
            return new FunnelRow(key, list.Count, applied, admitted, lost, list.Count == 0 ? 0 : Math.Round(100m * admitted / list.Count, 1));
        }

        return new EnquiryAnalytics(
            Row("All", rows.Select(r => r.Stage)),
            rows.GroupBy(r => r.Source).Select(g => Row(g.Key.ToString(), g.Select(r => r.Stage))).OrderByDescending(r => r.Enquiries).ToList(),
            rows.GroupBy(r => r.AssignedToUserId)
                .Select(g => Row(g.Key is { } u ? users.GetValueOrDefault(u, "Unknown") : "Unassigned", g.Select(r => r.Stage)))
                .OrderByDescending(r => r.Enquiries).ToList(),
            rows.GroupBy(r => r.Stage).ToDictionary(g => g.Key.ToString(), g => g.Count()));
    }

    private async Task<Enquiry> Load(Guid id, CancellationToken ct) =>
        await db.Enquiries.Include(e => e.Activities).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Enquiry", id);

    private static EnquiryDetail ToDetail(Enquiry e) => new(e.Id, e.EnquiryNo, e.CampusId, e.Source, e.CampaignCode, e.AcademicYearId, e.YearGroupId,
        e.ChildName, e.ChildDateOfBirth, e.GuardianName, e.GuardianPhone, e.GuardianEmail, e.Message, e.Stage, e.AssignedToUserId, e.LostReason, e.ApplicationId,
        e.CreatedAtUtc, e.Activities.OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new ActivityView(a.Id, a.Type, a.Text, a.DueAtUtc, a.IsCompleted, a.AssignedToUserId, a.CreatedAtUtc, a.CreatedBy)).ToList());
}

/// <summary>Shared by the staff and public (website) enquiry endpoints.</summary>
public sealed class EnquiryIntake(AppDbContext db, INumberSequenceService numbers, INotificationService notifications)
{
    public async Task<Enquiry> CreateAsync(EnquiryRequest r, CancellationToken ct)
    {
        var campus = await db.Campuses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CampusId, ct) ?? throw new NotFoundException("Campus", r.CampusId);
        var phone = StudentImportService.NormalisePhone(r.GuardianPhone) ?? r.GuardianPhone;
        var e = new Enquiry
        {
            EnquiryNo = await numbers.NextAsync(NumberKeys.Enquiry, ct: ct),
            CampusId = r.CampusId, Source = r.Source, CampaignCode = r.CampaignCode, AcademicYearId = r.AcademicYearId, YearGroupId = r.YearGroupId,
            ChildName = r.ChildName, ChildDateOfBirth = r.ChildDateOfBirth, GuardianName = r.GuardianName, GuardianPhone = phone,
            GuardianEmail = r.GuardianEmail, Message = r.Message
        };
        db.Enquiries.Add(e);
        await notifications.QueueAsync(new NotificationRequest
        {
            TemplateCode = TemplateCodes.EnquiryReceived, Channel = NotificationChannel.Sms, Recipient = phone, Language = "bn", CampusId = r.CampusId,
            Data = new Dictionary<string, string?> { ["GuardianName"] = r.GuardianName, ["ChildName"] = r.ChildName, ["CampusName"] = campus.Name, ["EnquiryNo"] = e.EnquiryNo },
            RelatedEntityType = nameof(Enquiry), RelatedEntityId = e.Id.ToString()
        }, ct);
        await db.SaveChangesAsync(ct);
        return e;
    }

    public async Task<Application> ApplicationFromEnquiryAsync(Enquiry e, Guid? academicYearId, Guid? yearGroupId, CancellationToken ct)
    {
        var yearId = academicYearId ?? await db.AcademicYears.Where(y => y.IsCurrent).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct)
                     ?? throw new DomainException("Choose an academic year.", "year_required");
        var ygId = yearGroupId ?? throw new DomainException("Choose the class applied for.", "class_required");
        var names = e.ChildName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        var app = new Application
        {
            ApplicationNo = await numbers.NextAsync(NumberKeys.Application, e.CampusId, yearId, ct),
            CampusId = e.CampusId, AcademicYearId = yearId, YearGroupId = ygId, EnquiryId = e.Id,
            FirstName = names.ElementAtOrDefault(0) ?? e.ChildName, LastName = names.ElementAtOrDefault(1),
            DateOfBirth = e.ChildDateOfBirth ?? default
        };
        app.Guardians.Add(new ApplicationGuardian { ApplicationId = app.Id, Relationship = "Guardian", Name = e.GuardianName, Phone = e.GuardianPhone, Email = e.GuardianEmail, IsPrimaryContact = true });
        db.Applications.Add(app);
        e.ApplicationId = app.Id;
        if (e.Stage < EnquiryStage.Applied) e.Stage = EnquiryStage.Applied;
        await db.SaveChangesAsync(ct);
        return app;
    }
}

public record EnquiryQuery(Guid? CampusId, EnquiryStage? Stage, EnquirySource? Source, Guid? AssignedToUserId, bool? Unassigned, string? Search);
public record EnquiryListItem(Guid Id, string EnquiryNo, Guid CampusId, EnquirySource Source, string ChildName, string GuardianName, string GuardianPhone,
    Guid? YearGroupId, EnquiryStage Stage, Guid? AssignedToUserId, DateTime CreatedAtUtc, DateTime? NextTaskDueUtc);
public record ActivityView(Guid Id, EnquiryActivityType Type, string Text, DateTime? DueAtUtc, bool IsCompleted, Guid? AssignedToUserId, DateTime CreatedAtUtc, Guid? CreatedBy);
public record EnquiryDetail(Guid Id, string EnquiryNo, Guid CampusId, EnquirySource Source, string? CampaignCode, Guid? AcademicYearId, Guid? YearGroupId,
    string ChildName, DateOnly? ChildDateOfBirth, string GuardianName, string GuardianPhone, string? GuardianEmail, string? Message, EnquiryStage Stage,
    Guid? AssignedToUserId, string? LostReason, Guid? ApplicationId, DateTime CreatedAtUtc, IReadOnlyList<ActivityView> Activities);
public record EnquiryRequest(Guid CampusId, EnquirySource Source, string? CampaignCode, Guid? AcademicYearId, Guid? YearGroupId, string ChildName,
    DateOnly? ChildDateOfBirth, string GuardianName, string GuardianPhone, string? GuardianEmail, string? Message);
public record StageChangeRequest(EnquiryStage Stage, string? LostReason, string? Note);
public record AssignRequest(Guid? UserId);
public record ActivityRequest(EnquiryActivityType Type, string Text, DateTime? DueAtUtc, Guid? AssignedToUserId, bool MarkCampusVisit = false);
public record ConvertRequest(Guid? AcademicYearId, Guid? YearGroupId);
public record TaskItem(Guid ActivityId, Guid EnquiryId, string EnquiryNo, string ChildName, string GuardianPhone, string Text, DateTime? DueAtUtc, bool IsOverdue);
public record FunnelRow(string Key, int Enquiries, int Applied, int Admitted, int Lost, decimal ConversionPercent);
public record EnquiryAnalytics(FunnelRow Total, IReadOnlyList<FunnelRow> BySource, IReadOnlyList<FunnelRow> ByCounsellor, IReadOnlyDictionary<string, int> ByStage);

public sealed class EnquiryRequestValidator : AbstractValidator<EnquiryRequest>
{
    public EnquiryRequestValidator()
    {
        RuleFor(x => x.ChildName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GuardianName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GuardianPhone).NotEmpty().Must(p => StudentImportService.NormalisePhone(p) is not null)
            .WithMessage("Enter a valid Bangladeshi mobile number (01XXXXXXXXX).");
        RuleFor(x => x.GuardianEmail).EmailAddress().When(x => !string.IsNullOrEmpty(x.GuardianEmail));
        RuleFor(x => x.Message).MaximumLength(2000);
    }
}

public sealed class ActivityRequestValidator : AbstractValidator<ActivityRequest>
{
    public ActivityRequestValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.DueAtUtc).NotNull().When(x => x.Type == EnquiryActivityType.Task).WithMessage("A task needs a due date.");
    }
}
