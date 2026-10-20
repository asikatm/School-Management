using SchoolManagement.Api.Modules.Admission.Services;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Infrastructure.Documents;
using SchoolManagement.Infrastructure.Localization;
using SchoolManagement.Infrastructure.Numbering;

namespace SchoolManagement.Api.Modules.Admission;

/// <summary>ADM-04: admission test sessions per class — rooms, invigilators, admit cards, seat numbers and scores.</summary>
[ApiController]
[Route("api/admission/test-sessions")]
[Tags("Admission: Tests")]
public sealed class AdmissionTestsController(
    AppDbContext db,
    INumberSequenceService numbers,
    AdmissionNotifier notifier,
    IQrCodeService qr,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Admission.View)]
    public async Task<List<TestSessionView>> List([FromQuery] Guid? campusId, [FromQuery] Guid? academicYearId, [FromQuery] Guid? yearGroupId, CancellationToken ct) =>
        await db.TestSessions.AsNoTracking()
            .Where(s => (campusId == null || s.CampusId == campusId) && (academicYearId == null || s.AcademicYearId == academicYearId) && (yearGroupId == null || s.YearGroupId == yearGroupId))
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => new TestSessionView(s.Id, s.CampusId, s.AcademicYearId, s.YearGroupId, s.Name, s.StartsAtUtc, s.DurationMinutes, s.Room,
                s.InvigilatorName, s.Capacity, s.MaxScore, s.Candidates.Count, s.Candidates.Count(c => c.Score != null)))
            .ToListAsync(ct);

    [HttpPost]
    [HasPermission(Permissions.Admission.Create)]
    public async Task<Guid> Create(TestSessionRequest r, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Admission.Create, r.CampusId);
        var s = new TestSession
        {
            CampusId = r.CampusId, AcademicYearId = r.AcademicYearId, YearGroupId = r.YearGroupId, Name = r.Name, StartsAtUtc = r.StartsAtUtc,
            DurationMinutes = r.DurationMinutes, Room = r.Room, InvigilatorName = r.InvigilatorName, InvigilatorUserId = r.InvigilatorUserId,
            Capacity = r.Capacity, MaxScore = r.MaxScore
        };
        db.TestSessions.Add(s);
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    [HttpGet("{id:guid}/candidates")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<List<CandidateView>> Candidates(Guid id, CancellationToken ct)
    {
        await LoadSession(id, ct);
        return await db.TestCandidates.AsNoTracking().Where(c => c.TestSessionId == id).OrderBy(c => c.SeatNo)
            .Select(c => new CandidateView(c.ApplicationId, c.Application!.ApplicationNo, c.Application.FirstName + " " + (c.Application.LastName ?? ""),
                c.AdmitCardNo, c.SeatNo, c.Attended, c.Score, c.Remarks))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Assigns applicants to the session (by id, or all unscheduled submitted applicants for the class when none are given),
    /// generates admit card and seat numbers and sends SMS reminders.
    /// </summary>
    [HttpPost("{id:guid}/candidates")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<List<CandidateView>> AssignCandidates(Guid id, AssignCandidatesRequest request, CancellationToken ct)
    {
        var session = await LoadSession(id, ct);
        var existing = await db.TestCandidates.Where(c => c.TestSessionId == id).CountAsync(ct);

        var q = db.Applications.Include(a => a.Guardians)
            .Where(a => a.CampusId == session.CampusId && a.AcademicYearId == session.AcademicYearId && a.YearGroupId == session.YearGroupId);
        q = request.ApplicationIds is { Count: > 0 }
            ? q.Where(a => request.ApplicationIds.Contains(a.Id))
            : q.Where(a => (a.Status == ApplicationStatus.Submitted || a.Status == ApplicationStatus.UnderReview) &&
                           !db.TestCandidates.Any(c => c.ApplicationId == a.Id));
        var apps = await q.OrderBy(a => a.SubmittedAtUtc).ToListAsync(ct);

        if (existing + apps.Count > session.Capacity)
            throw new DomainException($"Only {session.Capacity - existing} seats remain in this session.", "session_full", isConflict: true);

        var seat = existing;
        var dhakaTime = BanglaFormat.ToDhaka(session.StartsAtUtc).ToString("dd MMM yyyy, hh:mm tt");
        foreach (var app in apps)
        {
            if (app.Status is not (ApplicationStatus.Submitted or ApplicationStatus.UnderReview or ApplicationStatus.TestScheduled))
                throw new DomainException($"{app.ApplicationNo} is {app.Status} and cannot sit the test.", "invalid_status", isConflict: true);
            var candidate = new TestCandidate
            {
                TestSessionId = id, ApplicationId = app.Id,
                AdmitCardNo = await numbers.NextAsync(NumberKeys.AdmitCard, session.CampusId, session.AcademicYearId, ct),
                SeatNo = (++seat).ToString("D3")
            };
            db.TestCandidates.Add(candidate);
            app.Status = ApplicationStatus.TestScheduled;
            await notifier.NotifyAsync(app, TemplateCodes.TestScheduled, new Dictionary<string, string?>
            {
                ["TestDate"] = dhakaTime, ["Room"] = session.Room, ["AdmitCardNo"] = candidate.AdmitCardNo
            }, ct);
        }
        await db.SaveChangesAsync(ct);
        return await Candidates(id, ct);
    }

    /// <summary>Records attendance and raw scores; the application keeps the score as a percentage of the session's maximum.</summary>
    [HttpPut("{id:guid}/scores")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<List<CandidateView>> RecordScores(Guid id, List<ScoreEntry> scores, CancellationToken ct)
    {
        var session = await LoadSession(id, ct);
        var ids = scores.Select(s => s.ApplicationId).ToList();
        var candidates = await db.TestCandidates.Include(c => c.Application).Where(c => c.TestSessionId == id && ids.Contains(c.ApplicationId)).ToListAsync(ct);
        foreach (var entry in scores)
        {
            var c = candidates.FirstOrDefault(x => x.ApplicationId == entry.ApplicationId)
                    ?? throw new DomainException($"Application {entry.ApplicationId} is not a candidate in this session.", "not_candidate");
            if (entry.Score is { } s && (s < 0 || s > session.MaxScore))
                throw new DomainException($"Score must be between 0 and {session.MaxScore}.", "invalid_score");
            c.Attended = entry.Attended;
            c.Score = entry.Attended ? entry.Score : null;
            c.Remarks = entry.Remarks;
            var app = c.Application!;
            app.TestScore = c.Score is { } raw ? Math.Round(raw / session.MaxScore * 100, 2) : null;
            if (app.Status == ApplicationStatus.TestScheduled && (c.Score is not null || !entry.Attended))
                app.Status = ApplicationStatus.Tested;
        }
        await db.SaveChangesAsync(ct);
        return await Candidates(id, ct);
    }

    /// <summary>Admit-card data for printing (photo, QR and seat); a templated PDF arrives with the DOC module.</summary>
    [HttpGet("{id:guid}/admit-cards")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<List<AdmitCardView>> AdmitCards(Guid id, CancellationToken ct)
    {
        var session = await LoadSession(id, ct);
        var campus = await db.Campuses.Where(c => c.Id == session.CampusId).Select(c => c.Name).FirstAsync(ct);
        var cls = await db.YearGroups.Where(y => y.Id == session.YearGroupId).Select(y => y.Name).FirstAsync(ct);
        return await db.TestCandidates.AsNoTracking().Where(c => c.TestSessionId == id).OrderBy(c => c.SeatNo)
            .Select(c => new AdmitCardView(c.AdmitCardNo, c.SeatNo, c.Application!.ApplicationNo, c.Application.FirstName + " " + (c.Application.LastName ?? ""),
                cls, campus, session.Name, session.StartsAtUtc, session.DurationMinutes, session.Room, $"SMS-ADMIT:{c.AdmitCardNo}"))
            .ToListAsync(ct);
    }

    [HttpGet("admit-cards/{admitCardNo}/qr")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<IActionResult> AdmitCardQr(string admitCardNo, CancellationToken ct)
    {
        var exists = await db.TestCandidates.AnyAsync(c => c.AdmitCardNo == admitCardNo && db.Applications.Any(a => a.Id == c.ApplicationId), ct);
        if (!exists) throw new NotFoundException("Admit card", admitCardNo);
        return File(qr.Png($"SMS-ADMIT:{admitCardNo}"), "image/png");
    }

    private async Task<TestSession> LoadSession(Guid id, CancellationToken ct) =>
        await db.TestSessions.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Test session", id);
}

public record TestSessionView(Guid Id, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string Name, DateTime StartsAtUtc, int DurationMinutes, string? Room,
    string? InvigilatorName, int Capacity, decimal MaxScore, int Candidates, int Scored);
public record TestSessionRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string Name, DateTime StartsAtUtc, int DurationMinutes, string? Room,
    string? InvigilatorName, Guid? InvigilatorUserId, int Capacity, decimal MaxScore = 100);
public record CandidateView(Guid ApplicationId, string ApplicationNo, string Name, string AdmitCardNo, string? SeatNo, bool? Attended, decimal? Score, string? Remarks);
public record AssignCandidatesRequest(List<Guid>? ApplicationIds);
public record ScoreEntry(Guid ApplicationId, bool Attended, decimal? Score, string? Remarks);
public record AdmitCardView(string AdmitCardNo, string? SeatNo, string ApplicationNo, string Name, string ClassName, string CampusName, string SessionName,
    DateTime StartsAtUtc, int DurationMinutes, string? Room, string QrPayload);

public sealed class TestSessionRequestValidator : AbstractValidator<TestSessionRequest>
{
    public TestSessionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(10, 480);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 1000);
        RuleFor(x => x.MaxScore).GreaterThan(0);
    }
}
