using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Domain.Attendance;

namespace SchoolManagement.Api.Modules.Attendance;

public record ChildSummary(Guid StudentId, string AdmissionNo, string Name, string? ClassName, Guid CampusId, AttendanceMark? Today, string? TermName,
    decimal? TermPercent, int TermAbsent, int PendingLeave);

public record ChildDay(DateOnly Date, string Code, bool SchoolDay, TimeOnly? ArrivalTime, string? Remarks);

public record ChildMonth(Guid StudentId, int Year, int Month, IReadOnlyList<ChildDay> Days, int Present, int Absent, int Late, int Leave, decimal Percent);

/// <summary>Parent portal (PRT) — a guardian's own children: attendance at a glance and leave applications (ATT-04).</summary>
[ApiController]
[Route("api/portal")]
[Tags("Parent portal")]
[HasPermission(Permissions.Portal.Parent)]
public sealed class ParentPortalController(AppDbContext db, StudentLeaveService leaves, SchoolDays schoolDays, AcademicLookups lookups, AttendanceHistory history)
    : ControllerBase
{
    [HttpGet("children")]
    public async Task<List<ChildSummary>> Children(CancellationToken ct)
    {
        var ids = await leaves.GuardianChildrenAsync(ct);
        var today = lookups.TodayDhaka;
        var students = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.Id) && !s.IsDeleted)
            .Select(s => new { s.Id, s.AdmissionNo, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName), s.CampusId }).ToListAsync(ct);
        var term = await db.Terms.AsNoTracking().Where(t => t.StartDate <= today && t.EndDate >= today).FirstOrDefaultAsync(ct);
        var from = term?.StartDate ?? today;
        var marks = await db.StudentAttendances.IgnoreQueryFilters().AsNoTracking()
            .Where(a => ids.Contains(a.StudentId) && a.Date >= from && a.Date <= today)
            .Select(a => new { a.StudentId, a.Date, a.Mark }).ToListAsync(ct);
        var pending = await db.StudentLeaveRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(l => !l.IsDeleted && ids.Contains(l.StudentId) && l.Status == LeaveStatus.Pending).GroupBy(l => l.StudentId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var result = new List<ChildSummary>();
        foreach (var s in students.OrderBy(s => s.Name))
        {
            var sectionId = await schoolDays.SectionOfAsync(s.Id, today, ct);
            var label = sectionId is { } sid ? (await lookups.SectionsAsync([sid], ct)).GetValueOrDefault(sid)?.Label : null;
            var mine = marks.Where(m => m.StudentId == s.Id).ToList();
            var termMarks = term is null ? [] : mine.Where(m => m.Date >= term.StartDate).ToList();
            result.Add(new ChildSummary(s.Id, s.AdmissionNo, s.Name, label, s.CampusId, mine.FirstOrDefault(m => m.Date == today)?.Mark, term?.Name,
                term is null || termMarks.Count == 0 ? null : SchoolDays.Percent(termMarks.Count(m => m.Mark is AttendanceMark.Present or AttendanceMark.Late), termMarks.Count),
                termMarks.Count(m => m.Mark == AttendanceMark.Absent), pending.GetValueOrDefault(s.Id)));
        }
        return result;
    }

    [HttpGet("children/{studentId:guid}/attendance")]
    public async Task<ChildMonth> Attendance(Guid studentId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        await leaves.EnsureGuardianOfAsync(studentId, ct);
        return await history.MonthAsync(studentId, year, month, ct);
    }

    [HttpGet("leave")]
    public Task<List<StudentLeaveView>> Leave(CancellationToken ct) => leaves.ForGuardianAsync(ct);

    [HttpPost("children/{studentId:guid}/leave")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<StudentLeaveView> Apply(Guid studentId, [FromForm] StudentLeaveForm form, CancellationToken ct) => leaves.ApplyAsync(studentId, form, true, ct);

    [HttpPost("leave/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await leaves.CancelAsync(id, ct);
        return NoContent();
    }

    [HttpGet("leave/{id:guid}/attachment")]
    public async Task<IActionResult> Attachment(Guid id, CancellationToken ct)
    {
        var (content, type, name) = await leaves.AttachmentAsync(id, true, ct);
        return File(content, type, name);
    }
}
