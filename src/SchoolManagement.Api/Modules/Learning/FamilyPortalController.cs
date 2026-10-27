using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Api.Modules.Attendance;
using SchoolManagement.Api.Modules.Communication;
using SchoolManagement.Api.Modules.Exams;
using SchoolManagement.Domain.Attendance;
using SchoolManagement.Domain.Learning;
using SchoolManagement.Infrastructure.Storage;

namespace SchoolManagement.Api.Modules.Learning;

public record PortalStudent(Guid StudentId, string AdmissionNo, string Name, string? ClassName, Guid? SectionId, Guid CampusId, bool IsSelf);

public record PortalTimetable(string? SectionLabel, string? ClassTeacher, IReadOnlyList<DayOfWeek> Days, IReadOnlyList<PeriodView> Periods, IReadOnlyList<SlotView> Slots);

/// <summary>The student dashboard: who, today, this term, what is next.</summary>
public record PortalOverview(Guid StudentId, string Name, string? NameBn, string AdmissionNo, string? ClassName, int? RollNo, string CampusName, bool HasPhoto,
    string? BloodGroup, string? House, AttendanceMark? Today, TermAttendance Term, int HomeworkPending, IReadOnlyList<SlotView> TodayClasses,
    PortalExam? NextExam, StudentResultSummary? LatestResult, IReadOnlyList<NoticeView> LatestNotices);

/// <summary>
/// PRT for parents and students: homework (with online hand-in), the class diary, learning resources, exam date sheets,
/// admit cards and published results. A guardian sees their children; a student sees themself.
/// </summary>
[ApiController]
[Route("api/portal/students")]
[Tags("Portal: Learning & exams")]
[Authorize]
public sealed class FamilyPortalController(
    AppDbContext db,
    PortalAccess portal,
    AcademicLookups lookups,
    SchoolDays schoolDays,
    HomeworkService homework,
    DiaryService diary,
    ResourceService resources,
    ExamService exams,
    ResultService results,
    AttendanceHistory attendance,
    TimetableService timetable,
    ClassRoster roster,
    NoticeService notices,
    IFileStorage storage,
    ICurrentUser currentUser) : ControllerBase
{
    // ---- student dashboard

    [HttpGet("{studentId:guid}/overview")]
    public async Task<PortalOverview> Overview(Guid studentId, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        var today = lookups.TodayDhaka;
        var s = await db.Students.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == studentId, ct);
        var enrolment = await db.StudentEnrolments.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.StudentId == studentId && !e.IsDeleted && e.StartDate <= today && (e.EndDate == null || e.EndDate >= today))
            .OrderByDescending(e => e.StartDate).FirstOrDefaultAsync(ct);
        var section = enrolment?.SectionId is { } sid ? await lookups.SectionAsync(sid, ct) : null;
        var campus = await db.Campuses.IgnoreQueryFilters().Where(c => c.Id == s.CampusId).Select(c => c.Name).FirstAsync(ct);
        var house = s.HouseId is { } h ? await db.Houses.IgnoreQueryFilters().Where(x => x.Id == h).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var mark = await db.StudentAttendances.IgnoreQueryFilters().AsNoTracking().Where(a => a.StudentId == studentId && a.Date == today)
            .Select(a => (AttendanceMark?)a.Mark).FirstOrDefaultAsync(ct);
        var homeworkList = await homework.ForStudentAsync(studentId, false, ct);
        var routine = await RoutineAsync(studentId, ct);
        var upcoming = await exams.ForStudentAsync(studentId, ct);
        return new PortalOverview(s.Id, s.FullName, s.NameBn, s.AdmissionNo, section?.Label, enrolment?.RollNo, campus, s.PhotoPath is not null, s.BloodGroup, house, mark,
            await attendance.TermAsync(studentId, ct),
            homeworkList.Count(x => x.OnlineSubmission && x.Status is null or SubmissionStatus.ReturnedForRedo),
            routine.Slots.Where(x => x.Day == today.DayOfWeek).OrderBy(x => x.StartTime).ToList(),
            upcoming.Where(e => e.EndDate >= today).OrderBy(e => e.StartDate).FirstOrDefault(),
            (await results.ForStudentAsync(studentId, ct)).FirstOrDefault(),
            (await notices.ForStudentAsync(studentId, ct)).Take(5).ToList());
    }

    [HttpGet("{studentId:guid}/photo")]
    public async Task<IActionResult> Photo(Guid studentId, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        var path = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == studentId).Select(x => x.PhotoPath).FirstOrDefaultAsync(ct);
        if (path is null) return NotFound();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return File(storage.OpenRead(path), ext == ".png" ? "image/png" : ext == ".webp" ? "image/webp" : "image/jpeg");
    }

    // ---- attendance history (ATT-01/02 for the portal)

    [HttpGet("{studentId:guid}/attendance")]
    public async Task<ChildMonth> Attendance(Guid studentId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        return await attendance.MonthAsync(studentId, year, month, ct);
    }

    [HttpGet("{studentId:guid}/attendance/term")]
    public async Task<TermAttendance> TermAttendance(Guid studentId, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        return await attendance.TermAsync(studentId, ct);
    }

    // ---- class routine (ACA-08 for the portal): only the student's own subjects in option blocks

    [HttpGet("{studentId:guid}/timetable")]
    public async Task<PortalTimetable> Timetable(Guid studentId, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        return await RoutineAsync(studentId, ct);
    }

    // ---- notices

    [HttpGet("{studentId:guid}/notices")]
    public Task<List<NoticeView>> Notices(Guid studentId, CancellationToken ct) => notices.ForStudentAsync(studentId, ct);

    [HttpGet("{studentId:guid}/notices/{noticeId:guid}/attachment")]
    public async Task<IActionResult> NoticeAttachment(Guid studentId, Guid noticeId, CancellationToken ct)
    {
        var (content, type, name) = await notices.AttachmentAsync(noticeId, studentId, ct);
        return File(content, type, name);
    }

    private async Task<PortalTimetable> RoutineAsync(Guid studentId, CancellationToken ct)
    {
        if (await schoolDays.SectionOfAsync(studentId, lookups.TodayDhaka, ct) is not { } sectionId) return new PortalTimetable(null, null, [], [], []);
        var tt = await timetable.GetSectionAsync(sectionId, ct);
        var taken = new HashSet<Guid>();
        foreach (var subject in tt.Slots.Select(x => x.SubjectId).Distinct())
            if ((await roster.TakersAsync(tt.Section.YearGroupId, tt.Section.AcademicYearId, subject, [studentId], ct)).Count > 0) taken.Add(subject);
        var teacher = tt.Section.ClassTeacherUserId is { } t ? (await lookups.UserNamesAsync([t], ct)).GetValueOrDefault(t) : null;
        return new PortalTimetable(tt.Section.Label, teacher, tt.Days, tt.Periods, tt.Slots.Where(x => taken.Contains(x.SubjectId)).ToList());
    }

    [HttpGet]
    public async Task<List<PortalStudent>> Students(CancellationToken ct)
    {
        var ids = await portal.MyStudentsAsync(ct);
        var students = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.Id) && !s.IsDeleted)
            .Select(s => new { s.Id, s.AdmissionNo, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName), s.CampusId, s.UserId }).ToListAsync(ct);
        var result = new List<PortalStudent>();
        foreach (var s in students.OrderBy(s => s.Name))
        {
            var sectionId = await schoolDays.SectionOfAsync(s.Id, lookups.TodayDhaka, ct);
            var label = sectionId is { } sid ? (await lookups.SectionsAsync([sid], ct)).GetValueOrDefault(sid)?.Label : null;
            result.Add(new PortalStudent(s.Id, s.AdmissionNo, s.Name, label, sectionId, s.CampusId, s.UserId is not null && s.UserId == currentUser.UserId));
        }
        return result;
    }

    // ---- homework (LRN-02)

    [HttpGet("{studentId:guid}/homework")]
    public Task<List<StudentHomework>> Homework(Guid studentId, [FromQuery] bool past = false, CancellationToken ct = default) =>
        homework.ForStudentAsync(studentId, past, ct);

    [HttpPost("{studentId:guid}/homework/{assignmentId:guid}/submit")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<StudentHomework> Submit(Guid studentId, Guid assignmentId, [FromForm] SubmissionForm form, CancellationToken ct) =>
        homework.SubmitAsync(studentId, assignmentId, form, ct);

    [HttpGet("{studentId:guid}/homework/{assignmentId:guid}/attachment")]
    public async Task<IActionResult> HomeworkAttachment(Guid studentId, Guid assignmentId, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        var (content, type, name) = await homework.AttachmentAsync(assignmentId, ct);
        return File(content, type, name);
    }

    [HttpGet("{studentId:guid}/homework/{assignmentId:guid}/my-file")]
    public async Task<IActionResult> MyFile(Guid studentId, Guid assignmentId, CancellationToken ct)
    {
        var (content, type, name) = await homework.SubmissionFileAsync(assignmentId, studentId, ct);
        return File(content, type, name);
    }

    // ---- class diary (LRN-03)

    [HttpGet("{studentId:guid}/diary")]
    public async Task<ActionResult<DailyDigest>> Diary(Guid studentId, [FromQuery] DateOnly? date, CancellationToken ct) =>
        await diary.ForStudentAsync(studentId, date ?? lookups.TodayDhaka, ct) is { } digest ? digest : NoContent();

    // ---- resources (LRN-04)

    [HttpGet("{studentId:guid}/resources")]
    public Task<List<ResourceView>> Resources(Guid studentId, [FromQuery] Guid? subjectId, CancellationToken ct) => resources.ForStudentAsync(studentId, subjectId, ct);

    [HttpGet("{studentId:guid}/resources/{resourceId:guid}/file")]
    public async Task<IActionResult> ResourceFile(Guid studentId, Guid resourceId, CancellationToken ct)
    {
        var (content, type, name) = await resources.OpenAsync(resourceId, studentId, ct);
        return File(content, type, name);
    }

    [HttpPost("{studentId:guid}/resources/{resourceId:guid}/visit")]
    public Task<ResourceLink> ResourceVisit(Guid studentId, Guid resourceId, CancellationToken ct) => resources.VisitAsync(resourceId, studentId, ct);

    // ---- exams (EXM-01/02) and results (EXM-05/06)

    [HttpGet("{studentId:guid}/exams")]
    public async Task<List<PortalExam>> Exams(Guid studentId, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        return await exams.ForStudentAsync(studentId, ct);
    }

    [HttpGet("{studentId:guid}/exams/{examId:guid}/admit-card")]
    public async Task<ContentResult> AdmitCard(Guid studentId, Guid examId, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        if (!await db.Exams.IgnoreQueryFilters().AnyAsync(e => e.Id == examId && !e.IsDeleted && e.SchedulePublished, ct))
            throw new ForbiddenException("The admit card is not available yet.");
        return Content(await exams.AdmitCardsAsync(examId, null, null, studentId, ct), "text/html; charset=utf-8");
    }

    [HttpGet("{studentId:guid}/results")]
    public Task<List<StudentResultSummary>> Results(Guid studentId, CancellationToken ct) => results.ForStudentAsync(studentId, ct);

    [HttpGet("{studentId:guid}/results/{examId:guid}/report-card")]
    public async Task<ContentResult> ReportCard(Guid studentId, Guid examId, CancellationToken ct) =>
        Content(await results.ReportCardForStudentAsync(examId, studentId, $"{Request.Scheme}://{Request.Host}", ct), "text/html; charset=utf-8");

    [HttpGet("{studentId:guid}/transcript")]
    public async Task<ContentResult> Transcript(Guid studentId, CancellationToken ct) =>
        Content(await results.TranscriptHtmlAsync(studentId, true, ct), "text/html; charset=utf-8");
}
