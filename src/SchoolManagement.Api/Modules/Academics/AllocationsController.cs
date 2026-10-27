using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Core;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Academics;

public record AllocationView(Guid Id, Guid SectionId, string SectionLabel, Guid SubjectId, string SubjectCode, string SubjectName, Guid TeacherUserId,
    string TeacherName, int PeriodsPerWeek, int Placed);

public record AllocationRowRequest(Guid SubjectId, Guid TeacherUserId, int PeriodsPerWeek);

public enum LoadStatus
{
    Ok = 0,
    Overloaded = 1,
    UnderUsed = 2
}

public record TeacherLoad(Guid TeacherUserId, string Name, int AllocatedPeriods, int PlacedPeriods, int Sections, IReadOnlyList<string> Subjects, LoadStatus Status);

public record AllocationGap(Guid SectionId, string SectionLabel, Guid SubjectId, string SubjectName, SubjectType Type, int PeriodsPerWeek);

/// <summary>ACA-03: teacher–subject–class allocation with periods per week, and the teacher-load report.</summary>
[ApiController]
[Route("api/academics/allocations")]
[Tags("Academics: Allocation")]
public sealed class AllocationsController(AppDbContext db, AcademicLookups lookups, ISettingsService settings, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Academic.View)]
    public async Task<List<AllocationView>> List([FromQuery] Guid academicYearId, [FromQuery] Guid? campusId, [FromQuery] Guid? sectionId,
        [FromQuery] Guid? teacherUserId, CancellationToken ct)
    {
        var q = db.TeacherAllocations.AsNoTracking().Where(a => a.AcademicYearId == academicYearId);
        if (campusId is not null) q = q.Where(a => a.CampusId == campusId);
        if (sectionId is not null) q = q.Where(a => a.SectionId == sectionId);
        if (teacherUserId is not null) q = q.Where(a => a.TeacherUserId == teacherUserId);
        return await ViewsAsync(await q.ToListAsync(ct), ct);
    }

    /// <summary>Replaces a section's allocations. A subject may be split between teachers (e.g. theory and practical).</summary>
    [HttpPut("section/{sectionId:guid}")]
    [HasPermission(Permissions.Academic.Manage)]
    public async Task<List<AllocationView>> SaveSection(Guid sectionId, List<AllocationRowRequest> rows, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        currentUser.EnsurePermission(Permissions.Academic.Manage, section.CampusId);

        var teacherIds = rows.Select(r => r.TeacherUserId).Distinct().ToList();
        var known = await db.Users.Where(u => teacherIds.Contains(u.Id) && u.IsActive && u.UserType == UserType.Staff).CountAsync(ct);
        if (known != teacherIds.Count) throw new DomainException("Every teacher must be an active staff user.", "invalid_teacher");
        if (rows.GroupBy(r => (r.SubjectId, r.TeacherUserId)).Any(g => g.Count() > 1))
            throw new DomainException("The same teacher is listed twice for one subject.", "duplicate");

        db.TeacherAllocations.RemoveRange(await db.TeacherAllocations.Where(a => a.SectionId == sectionId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        foreach (var r in rows.Where(r => r.PeriodsPerWeek > 0))
            db.TeacherAllocations.Add(new TeacherAllocation
            {
                CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, SectionId = sectionId,
                SubjectId = r.SubjectId, TeacherUserId = r.TeacherUserId, PeriodsPerWeek = r.PeriodsPerWeek
            });
        await db.SaveChangesAsync(ct);
        return await List(section.AcademicYearId, null, sectionId, null, ct);
    }

    /// <summary>Periods per teacher across all their sections, flagged against the school's weekly minimum and maximum.</summary>
    [HttpGet("load")]
    [HasPermission(Permissions.Academic.View)]
    public async Task<List<TeacherLoad>> Load([FromQuery] Guid academicYearId, [FromQuery] Guid? campusId, CancellationToken ct)
    {
        var max = await settings.GetIntAsync(SettingKeys.MaxTeacherPeriodsPerWeek, 30, campusId, ct);
        var min = await settings.GetIntAsync(SettingKeys.MinTeacherPeriodsPerWeek, 12, campusId, ct);
        // Load counts every campus a teacher works on; the campus filter only chooses which teachers to list.
        var all = await db.TeacherAllocations.IgnoreQueryFilters().AsNoTracking().Where(a => a.AcademicYearId == academicYearId).ToListAsync(ct);
        var placed = await db.TimetableSlots.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.AcademicYearId == academicYearId && s.TeacherUserId != null)
            .GroupBy(s => s.TeacherUserId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var visible = campusId is null
            ? all.Where(a => currentUser.CanAccessCampus(a.CampusId)).Select(a => a.TeacherUserId)
            : all.Where(a => a.CampusId == campusId).Select(a => a.TeacherUserId);
        var teachers = visible.Distinct().ToList();
        var names = await lookups.UserNamesAsync(teachers.Select(t => (Guid?)t), ct);
        var subjects = await lookups.SubjectsAsync(ct);

        return teachers.Select(t =>
        {
            var mine = all.Where(a => a.TeacherUserId == t).ToList();
            var total = mine.Sum(a => a.PeriodsPerWeek);
            return new TeacherLoad(t, names.GetValueOrDefault(t, "?"), total, placed.GetValueOrDefault(t), mine.Select(a => a.SectionId).Distinct().Count(),
                mine.Select(a => subjects.GetValueOrDefault(a.SubjectId).Code ?? "?").Distinct().Order().ToList(),
                total > max ? LoadStatus.Overloaded : total < min ? LoadStatus.UnderUsed : LoadStatus.Ok);
        }).OrderByDescending(l => l.AllocatedPeriods).ToList();
    }

    /// <summary>Curriculum subjects with no teacher yet, per section (electives only when students in the section chose them).</summary>
    [HttpGet("gaps")]
    [HasPermission(Permissions.Academic.View)]
    public async Task<List<AllocationGap>> Gaps([FromQuery] Guid academicYearId, [FromQuery] Guid? campusId, CancellationToken ct)
    {
        var sectionQ = db.Sections.AsNoTracking().Where(s => s.AcademicYearId == academicYearId);
        if (campusId is not null) sectionQ = sectionQ.Where(s => s.CampusId == campusId);
        var sections = await sectionQ.Select(s => new { s.Id, s.YearGroupId }).ToListAsync(ct);
        var ids = sections.Select(s => s.Id).ToList();
        var labels = await lookups.SectionsAsync(ids, ct);
        var curriculum = await db.CurriculumSubjects.AsNoTracking().Include(c => c.Subject).ToListAsync(ct);
        var allocated = (await db.TeacherAllocations.AsNoTracking().Where(a => ids.Contains(a.SectionId)).Select(a => new { a.SectionId, a.SubjectId }).ToListAsync(ct))
            .Select(a => (a.SectionId, a.SubjectId)).ToHashSet();
        var chosen = (await db.StudentSubjects.AsNoTracking().Where(s => s.AcademicYearId == academicYearId)
                .Join(db.StudentEnrolments.Where(e => e.Status == Domain.Students.EnrolmentStatus.Active && e.SectionId != null), s => s.StudentId, e => e.StudentId,
                    (s, e) => new { SectionId = e.SectionId!.Value, s.SubjectId })
                .Distinct().ToListAsync(ct))
            .Select(x => (x.SectionId, x.SubjectId)).ToHashSet();

        return sections.SelectMany(s => curriculum.Where(c => c.YearGroupId == s.YearGroupId)
                .Where(c => c.Type == SubjectType.Compulsory || chosen.Contains((s.Id, c.SubjectId)))
                .Where(c => !allocated.Contains((s.Id, c.SubjectId)))
                .Select(c => new AllocationGap(s.Id, labels.GetValueOrDefault(s.Id)?.Label ?? "?", c.SubjectId, c.Subject!.Name, c.Type, c.PeriodsPerWeek)))
            .OrderBy(g => g.SectionLabel).ToList();
    }

    private async Task<List<AllocationView>> ViewsAsync(List<TeacherAllocation> rows, CancellationToken ct)
    {
        var sections = await lookups.SectionsAsync(rows.Select(r => r.SectionId), ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var names = await lookups.UserNamesAsync(rows.Select(r => (Guid?)r.TeacherUserId), ct);
        var sectionIds = rows.Select(r => r.SectionId).Distinct().ToList();
        var placed = await db.TimetableSlots.AsNoTracking().Where(s => sectionIds.Contains(s.SectionId))
            .GroupBy(s => new { s.SectionId, s.SubjectId, s.TeacherUserId }).Select(g => new { g.Key.SectionId, g.Key.SubjectId, g.Key.TeacherUserId, Count = g.Count() })
            .ToListAsync(ct);
        return rows.Select(r =>
        {
            var (code, name) = subjects.GetValueOrDefault(r.SubjectId, ("?", "?"));
            return new AllocationView(r.Id, r.SectionId, sections.GetValueOrDefault(r.SectionId)?.Label ?? "?", r.SubjectId, code, name, r.TeacherUserId,
                names.GetValueOrDefault(r.TeacherUserId, "?"), r.PeriodsPerWeek,
                placed.FirstOrDefault(p => p.SectionId == r.SectionId && p.SubjectId == r.SubjectId && p.TeacherUserId == r.TeacherUserId)?.Count ?? 0);
        }).OrderBy(v => v.SectionLabel).ThenBy(v => v.SubjectName).ToList();
    }
}

public sealed class AllocationRowRequestValidator : AbstractValidator<AllocationRowRequest>
{
    public AllocationRowRequestValidator() => RuleFor(x => x.PeriodsPerWeek).InclusiveBetween(0, 20);
}
