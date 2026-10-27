using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Api.Modules.Attendance;
using SchoolManagement.Domain.Learning;

namespace SchoolManagement.Api.Modules.Learning;

public record DiaryEntryRequest(Guid SectionId, DateOnly Date, Guid? SubjectId, string? Topics, string? Homework, string? Reminders, string? KitNotes);

public record DiaryEntryView(Guid Id, Guid SectionId, DateOnly Date, Guid? SubjectId, string? SubjectName, string? Topics, string? Homework, string? Reminders,
    string? KitNotes, string? PostedBy, DateTime PostedAtUtc, bool CanEdit);

public record DigestHomework(Guid AssignmentId, string SubjectName, string Title, DateTime DueAtLocal);

/// <summary>LRN-03: the day's diary of a class with the homework set that day and the homework due next.</summary>
public record DailyDigest(Guid SectionId, string SectionLabel, DateOnly Date, string? ClosedReason, IReadOnlyList<DiaryEntryView> Entries,
    IReadOnlyList<DigestHomework> HomeworkSet, IReadOnlyList<DigestHomework> DueNext, bool CanWrite);

/// <summary>LRN-03: class diary — topics covered, homework, reminders and uniform/kit notes, shown to parents as a daily digest.</summary>
public sealed class DiaryService(
    AppDbContext db,
    AcademicLookups lookups,
    SchoolDays schoolDays,
    ClassRoster roster,
    TeachingAccess access,
    PortalAccess portal,
    ICurrentUser currentUser)
{
    public async Task<DailyDigest> DigestAsync(Guid sectionId, DateOnly date, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        access.EnsureCampus(section.CampusId);
        return await BuildAsync(section, date, null, ct);
    }

    public async Task<DiaryEntryView> CreateAsync(DiaryEntryRequest r, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(r.SectionId, ct);
        await access.EnsureAsync(section, r.SubjectId, Permissions.Learning.Teach, Permissions.Learning.Manage, ct);
        Validate(r);
        var e = new ClassDiaryEntry { CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, SectionId = section.Id, Date = r.Date, PostedByUserId = currentUser.UserId };
        Apply(e, r);
        db.ClassDiaryEntries.Add(e);
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([e], ct))[0];
    }

    public async Task<DiaryEntryView> UpdateAsync(Guid id, DiaryEntryRequest r, CancellationToken ct)
    {
        var e = await db.ClassDiaryEntries.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Diary entry", id);
        await EnsureCanEditAsync(e, ct);
        Validate(r);
        Apply(e, r);
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([e], ct))[0];
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var e = await db.ClassDiaryEntries.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Diary entry", id);
        await EnsureCanEditAsync(e, ct);
        db.ClassDiaryEntries.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The digest for a student: their section on that day, subject lines only for subjects they take.</summary>
    public async Task<DailyDigest?> ForStudentAsync(Guid studentId, DateOnly date, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        if (await schoolDays.SectionOfAsync(studentId, date, ct) is not { } sectionId) return null;
        return await BuildAsync(await lookups.SectionAsync(sectionId, ct), date, studentId, ct);
    }

    // ------------------------------------------------------------------ internals

    private async Task<DailyDigest> BuildAsync(SectionInfo section, DateOnly date, Guid? studentId, CancellationToken ct)
    {
        var entries = await db.ClassDiaryEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => !e.IsDeleted && e.SectionId == section.Id && e.Date == date).OrderBy(e => e.CreatedAtUtc).ToListAsync(ct);
        var day = date.ToDateTime(TimeOnly.MinValue);
        var set = await db.Assignments.IgnoreQueryFilters().AsNoTracking().Where(a => !a.IsDeleted && a.SectionId == section.Id && a.SetOn == date).ToListAsync(ct);
        // "Due next": anything due from the end of this day up to the end of the next school day.
        var next = (await schoolDays.BetweenAsync(section.CampusId, date.AddDays(1), date.AddDays(10), ct)).FirstOrDefault(date.AddDays(1));
        var due = await db.Assignments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => !a.IsDeleted && a.SectionId == section.Id && a.DueAtLocal >= day.AddDays(1) && a.DueAtLocal < next.AddDays(1).ToDateTime(TimeOnly.MinValue)).ToListAsync(ct);

        if (studentId is { } sid)
        {
            var subjects = entries.Select(e => e.SubjectId).OfType<Guid>().Concat(set.Select(a => a.SubjectId)).Concat(due.Select(a => a.SubjectId)).Distinct();
            var taken = new HashSet<Guid>();
            foreach (var s in subjects)
                if ((await roster.TakersAsync(section.YearGroupId, section.AcademicYearId, s, [sid], ct)).Count > 0) taken.Add(s);
            entries = entries.Where(e => e.SubjectId is null || taken.Contains(e.SubjectId.Value)).ToList();
            set = set.Where(a => taken.Contains(a.SubjectId)).ToList();
            due = due.Where(a => taken.Contains(a.SubjectId)).ToList();
        }

        var names = await lookups.SubjectsAsync(ct);
        DigestHomework H(Assignment a) => new(a.Id, names.GetValueOrDefault(a.SubjectId).Name ?? "?", a.Title, a.DueAtLocal);
        var canWrite = studentId is null && await access.CanAsync(section, null, Permissions.Learning.Teach, Permissions.Learning.Manage, ct);
        return new DailyDigest(section.Id, section.Label, date, await schoolDays.ClosedReasonAsync(section.CampusId, date, ct), await ViewsAsync(entries, ct),
            set.OrderBy(a => a.DueAtLocal).Select(H).ToList(), due.OrderBy(a => a.DueAtLocal).Select(H).ToList(), canWrite);
    }

    private static void Validate(DiaryEntryRequest r)
    {
        if (new[] { r.Topics, r.Homework, r.Reminders, r.KitNotes }.All(string.IsNullOrWhiteSpace))
            throw new DomainException("Write at least one of: topics, homework, reminders or kit notes.", "empty_entry");
    }

    private static void Apply(ClassDiaryEntry e, DiaryEntryRequest r)
    {
        e.Date = r.Date;
        e.SubjectId = r.SubjectId;
        e.Topics = LearningText.Clean(r.Topics);
        e.Homework = LearningText.Clean(r.Homework);
        e.Reminders = LearningText.Clean(r.Reminders);
        e.KitNotes = LearningText.Clean(r.KitNotes);
    }

    private async Task EnsureCanEditAsync(ClassDiaryEntry e, CancellationToken ct)
    {
        if (currentUser.HasPermission(Permissions.Learning.Manage, e.CampusId)) return;
        var section = await lookups.SectionAsync(e.SectionId, ct);
        if (e.PostedByUserId != currentUser.UserId && section.ClassTeacherUserId != currentUser.UserId)
            throw new ForbiddenException("Only the teacher who wrote this entry or the class teacher can change it.");
        await access.EnsureAsync(section, e.SubjectId, Permissions.Learning.Teach, Permissions.Learning.Manage, ct);
    }

    private async Task<List<DiaryEntryView>> ViewsAsync(IReadOnlyList<ClassDiaryEntry> rows, CancellationToken ct)
    {
        var subjects = await lookups.SubjectsAsync(ct);
        var names = await lookups.UserNamesAsync(rows.Select(r => r.PostedByUserId), ct);
        var sections = await lookups.SectionsAsync(rows.Select(r => r.SectionId), ct);
        return rows.Select(e => new DiaryEntryView(e.Id, e.SectionId, e.Date, e.SubjectId, e.SubjectId is { } s ? subjects.GetValueOrDefault(s).Name : null, e.Topics, e.Homework,
            e.Reminders, e.KitNotes, e.PostedByUserId is { } p ? names.GetValueOrDefault(p) : null, e.CreatedAtUtc,
            currentUser.HasPermission(Permissions.Learning.Manage, e.CampusId)
            || (currentUser.HasPermission(Permissions.Learning.Teach, e.CampusId)
                && (e.PostedByUserId == currentUser.UserId || sections.GetValueOrDefault(e.SectionId)?.ClassTeacherUserId == currentUser.UserId)))).ToList();
    }
}
