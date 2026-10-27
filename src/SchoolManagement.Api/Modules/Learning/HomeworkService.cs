using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Api.Modules.Attendance;
using SchoolManagement.Domain.Learning;
using SchoolManagement.Infrastructure.Jobs;
using SchoolManagement.Infrastructure.Localization;
using SchoolManagement.Infrastructure.Settings;
using SchoolManagement.Infrastructure.Storage;

namespace SchoolManagement.Api.Modules.Learning;

public record AssignmentView(Guid Id, Guid CampusId, Guid SectionId, string SectionLabel, Guid SubjectId, string SubjectName, string Title, string? Instructions,
    DateOnly SetOn, DateTime DueAtLocal, decimal? MaxMarks, bool OnlineSubmission, bool AllowLate, decimal LatePenaltyPercent, bool HasAttachment,
    string? AttachmentName, string? PostedBy, int Students, int Submitted, int Graded, bool InGradebook, bool CanEdit);

public sealed class AssignmentForm
{
    public Guid SectionId { get; init; }
    public Guid SubjectId { get; init; }
    public required string Title { get; init; }
    public string? Instructions { get; init; }
    public DateTime DueAtLocal { get; init; }
    public decimal? MaxMarks { get; init; }
    public bool OnlineSubmission { get; init; } = true;
    public bool AllowLate { get; init; } = true;
    public decimal? LatePenaltyPercent { get; init; }
    public IFormFile? Attachment { get; init; }

    /// <summary>Also add the homework to the term gradebook in this category (needs MaxMarks).</summary>
    public Guid? GradebookCategoryId { get; init; }
}

public record SubmissionRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, Guid? SubmissionId, DateTime? SubmittedAtUtc, bool IsLate, string? Text,
    bool HasFile, string? FileName, SubmissionStatus? Status, decimal? Marks, decimal? FinalMarks, string? Feedback);

public record AssignmentDetail(AssignmentView Assignment, IReadOnlyList<SubmissionRow> Students);

public record GradeEntry(Guid StudentId, decimal? Marks, string? Feedback, bool ReturnForRedo);

public record GradeRequest(IReadOnlyList<GradeEntry> Entries);

public sealed class SubmissionForm
{
    public string? Text { get; init; }
    public IFormFile? File { get; init; }
}

public record StudentHomework(Guid AssignmentId, string SubjectName, string Title, string? Instructions, DateOnly SetOn, DateTime DueAtLocal, decimal? MaxMarks,
    bool OnlineSubmission, bool AllowLate, decimal LatePenaltyPercent, bool HasAttachment, string? AttachmentName, bool Overdue, SubmissionStatus? Status,
    DateTime? SubmittedAtUtc, bool IsLate, string? SubmittedText, string? SubmittedFileName, decimal? FinalMarks, string? Feedback, bool CanSubmit);

/// <summary>
/// LRN-02: teachers post homework with a due time and attachment; students (or their guardians) hand it in online;
/// the teacher grades with comments. Late work is refused or marked down by the assignment's late rule.
/// </summary>
public sealed class HomeworkService(
    AppDbContext db,
    AcademicLookups lookups,
    ClassRoster roster,
    TeachingAccess access,
    PortalAccess portal,
    GuardianNotifier notifier,
    ISettingsService settings,
    IFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    private DateTime NowLocal => BanglaFormat.ToDhaka(clock.GetUtcNow().UtcDateTime);

    public async Task<List<AssignmentView>> ListAsync(Guid? sectionId, Guid? subjectId, DateOnly? from, DateOnly? to, bool mine, CancellationToken ct)
    {
        var q = db.Assignments.AsNoTracking();
        if (sectionId is not null) q = q.Where(a => a.SectionId == sectionId);
        if (subjectId is not null) q = q.Where(a => a.SubjectId == subjectId);
        if (from is { } f) q = q.Where(a => a.DueAtLocal >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) q = q.Where(a => a.DueAtLocal < t.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (mine) q = q.Where(a => a.PostedByUserId == currentUser.UserId);
        var rows = await q.OrderByDescending(a => a.DueAtLocal).Take(300).ToListAsync(ct);
        return await ViewsAsync(rows, ct);
    }

    public async Task<AssignmentView> CreateAsync(AssignmentForm form, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(form.SectionId, ct);
        await access.EnsureAsync(section, form.SubjectId, Permissions.Learning.Teach, Permissions.Learning.Manage, ct);
        var today = lookups.TodayDhaka;
        if (form.DueAtLocal < today.ToDateTime(TimeOnly.MinValue)) throw new DomainException("The due date cannot be in the past.", "due_in_past");
        if (form.GradebookCategoryId is not null && form.MaxMarks is null) throw new DomainException("Give the maximum marks to add the homework to the gradebook.", "max_marks_required");

        var a = new Assignment
        {
            CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, SectionId = section.Id, SubjectId = form.SubjectId, Title = form.Title.Trim(),
            SetOn = today, PostedByUserId = currentUser.UserId,
            LatePenaltyPercent = form.LatePenaltyPercent ?? await settings.GetDecimalAsync(SettingKeys.HomeworkLatePenaltyPercent, 0, section.CampusId, ct)
        };
        Apply(a, form);
        await SaveAttachmentAsync(a, form.Attachment, ct);
        db.Assignments.Add(a);

        if (form.GradebookCategoryId is { } categoryId)
        {
            var term = await db.Terms.AsNoTracking().Where(t => t.AcademicYearId == section.AcademicYearId && t.StartDate <= DateOnly.FromDateTime(a.DueAtLocal) && t.EndDate >= DateOnly.FromDateTime(a.DueAtLocal))
                           .FirstOrDefaultAsync(ct) ?? throw new DomainException("No term covers the due date, so the homework cannot go into a term gradebook.", "no_term");
            db.GradebookItems.Add(new GradebookItem
            {
                CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, SectionId = section.Id, SubjectId = a.SubjectId, TermId = term.Id,
                CategoryId = categoryId, Title = a.Title, Date = DateOnly.FromDateTime(a.DueAtLocal), MaxMarks = a.MaxMarks!.Value, AssignmentId = a.Id
            });
        }

        // Appendix C: "homework assigned" → students and guardians, in-app.
        var subject = (await lookups.SubjectsAsync(ct)).GetValueOrDefault(a.SubjectId).Name;
        foreach (var s in await roster.TakingAsync(section, a.SubjectId, today, ct))
            await notifier.NotifyAsync(s.StudentId, a.CampusId, TemplateCodes.HomeworkAssigned, new Dictionary<string, string?>
            {
                ["ChildName"] = s.Name, ["ClassName"] = section.Label, ["Subject"] = subject, ["Title"] = a.Title, ["Due"] = a.DueAtLocal.ToString("dd MMM, hh:mm tt")
            }, nameof(Assignment), a.Id.ToString(), ct, sms: false, includeStudent: true);

        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([a], ct))[0];
    }

    public async Task<AssignmentView> UpdateAsync(Guid id, AssignmentForm form, CancellationToken ct)
    {
        var a = await db.Assignments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Homework", id);
        await EnsureCanEditAsync(a, ct);
        if (form.SectionId != a.SectionId || form.SubjectId != a.SubjectId) throw new DomainException("Class and subject cannot be changed; set new homework instead.", "fixed_class");
        Apply(a, form);
        if (form.LatePenaltyPercent is { } p) a.LatePenaltyPercent = p;
        if (form.DueAtLocal.Date != a.DueAtLocal.Date) a.ReminderSentAtUtc = null;
        await SaveAttachmentAsync(a, form.Attachment, ct);
        foreach (var item in await db.GradebookItems.Where(i => i.AssignmentId == a.Id).ToListAsync(ct))
        {
            item.Title = a.Title;
            if (a.MaxMarks is { } max) item.MaxMarks = max;
        }
        await RecalculateFinalMarksAsync(a, ct);
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([a], ct))[0];
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Assignments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Homework", id);
        await EnsureCanEditAsync(a, ct);
        var items = await db.GradebookItems.Where(i => i.AssignmentId == a.Id).ToListAsync(ct);
        if (items.Count > 0 && await LockedAsync(items[0], ct))
            throw new DomainException("The gradebook for this term is approved; the homework can no longer be removed.", "gradebook_locked", isConflict: true);
        db.GradebookItems.RemoveRange(items);
        db.Assignments.Remove(a);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AssignmentDetail> DetailAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Assignments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Homework", id);
        access.EnsureCampus(a.CampusId);
        var section = await lookups.SectionAsync(a.SectionId, ct);
        var students = await roster.TakingAsync(section, a.SubjectId, DateOnly.FromDateTime(a.DueAtLocal) < lookups.TodayDhaka ? DateOnly.FromDateTime(a.DueAtLocal) : lookups.TodayDhaka, ct);
        var subs = await db.AssignmentSubmissions.AsNoTracking().Where(s => s.AssignmentId == id).ToDictionaryAsync(s => s.StudentId, ct);
        // A student who has left still shows if they handed something in.
        foreach (var extra in subs.Keys.Where(k => students.All(s => s.StudentId != k)).ToList())
            if (await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => s.Id == extra).Select(s => new RosterStudent(s.Id, s.AdmissionNo, s.FirstName + (s.LastName == null ? "" : " " + s.LastName), null)).FirstOrDefaultAsync(ct) is { } st)
                students.Add(st);
        var rows = students.Select(s =>
        {
            var x = subs.GetValueOrDefault(s.StudentId);
            return new SubmissionRow(s.StudentId, s.AdmissionNo, s.Name, s.RollNo, x?.Id, x?.SubmittedAtUtc, x?.IsLate ?? false, x?.Text, x?.FilePath is not null, x?.FileName,
                x?.Status, x?.Marks, x?.FinalMarks, x?.Feedback);
        }).ToList();
        return new AssignmentDetail((await ViewsAsync([a], ct))[0], rows);
    }

    /// <summary>Marks and feedback for several students; work checked in class gets a mark without an online hand-in.</summary>
    public async Task<AssignmentDetail> GradeAsync(Guid id, GradeRequest r, CancellationToken ct)
    {
        var a = await db.Assignments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Homework", id);
        var section = await lookups.SectionAsync(a.SectionId, ct);
        await access.EnsureAsync(section, a.SubjectId, Permissions.Learning.Teach, Permissions.Learning.Manage, ct);
        var item = await db.GradebookItems.AsNoTracking().FirstOrDefaultAsync(i => i.AssignmentId == a.Id, ct);
        if (item is not null && await LockedAsync(item, ct))
            throw new DomainException("The term gradebook for this subject is approved; marks are locked.", "gradebook_locked", isConflict: true);

        var ids = r.Entries.Select(e => e.StudentId).ToList();
        var takers = await roster.TakersAsync(section.YearGroupId, section.AcademicYearId, a.SubjectId, ids, ct);
        var existing = await db.AssignmentSubmissions.Where(s => s.AssignmentId == id && ids.Contains(s.StudentId)).ToDictionaryAsync(s => s.StudentId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var e in r.Entries)
        {
            if (e.Marks is { } m && (m < 0 || (a.MaxMarks is { } max && m > max)))
                throw new DomainException($"Marks must be between 0 and {a.MaxMarks:0.##}.", "marks_out_of_range");
            if (!existing.TryGetValue(e.StudentId, out var s))
            {
                if (e.Marks is null && string.IsNullOrWhiteSpace(e.Feedback)) continue;
                if (!takers.Contains(e.StudentId)) throw new DomainException("A student in the request does not take this subject in the class.", "not_in_class");
                s = new AssignmentSubmission { CampusId = a.CampusId, AssignmentId = a.Id, StudentId = e.StudentId, SubmittedAtUtc = now, SubmittedByUserId = currentUser.UserId };
                db.AssignmentSubmissions.Add(s);
            }
            s.Marks = e.Marks;
            s.Feedback = LearningText.Clean(e.Feedback);
            s.FinalMarks = FinalMarks(e.Marks, s.IsLate, a.LatePenaltyPercent);
            s.Status = e.ReturnForRedo ? SubmissionStatus.ReturnedForRedo : e.Marks is null && s.Feedback is null ? SubmissionStatus.Submitted : SubmissionStatus.Graded;
            s.GradedByUserId = currentUser.UserId;
            s.GradedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
        return await DetailAsync(id, ct);
    }

    public async Task<(Stream Content, string ContentType, string FileName)> AttachmentAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Assignments.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct) ?? throw new NotFoundException("Homework", id);
        if (!currentUser.HasPermission(Permissions.Learning.View, a.CampusId)) await EnsurePortalSeesAsync(a, ct);
        if (a.AttachmentPath is null) throw new NotFoundException("Attachment", id);
        return (storage.OpenRead(a.AttachmentPath), a.AttachmentType ?? "application/octet-stream", a.AttachmentName ?? "attachment");
    }

    public async Task<(Stream Content, string ContentType, string FileName)> SubmissionFileAsync(Guid assignmentId, Guid studentId, CancellationToken ct)
    {
        var a = await db.Assignments.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == assignmentId && !x.IsDeleted, ct) ?? throw new NotFoundException("Homework", assignmentId);
        if (!currentUser.HasPermission(Permissions.Learning.View, a.CampusId)) await portal.EnsureAsync(studentId, ct);
        var s = await db.AssignmentSubmissions.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.AssignmentId == assignmentId && x.StudentId == studentId, ct);
        if (s?.FilePath is null) throw new NotFoundException("Submission file", studentId);
        return (storage.OpenRead(s.FilePath), s.FileType ?? "application/octet-stream", s.FileName ?? "submission");
    }

    // ------------------------------------------------------------------ portal (students and guardians)

    public async Task<List<StudentHomework>> ForStudentAsync(Guid studentId, bool includePast, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        var today = lookups.TodayDhaka;
        var sectionIds = await db.StudentEnrolments.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.StudentId == studentId && !e.IsDeleted && e.SectionId != null && (e.EndDate == null || e.EndDate >= today.AddDays(-60)))
            .Select(e => e.SectionId!.Value).Distinct().ToListAsync(ct);
        var since = (includePast ? today.AddDays(-60) : today.AddDays(-14)).ToDateTime(TimeOnly.MinValue);
        var rows = await db.Assignments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => !a.IsDeleted && sectionIds.Contains(a.SectionId) && a.DueAtLocal >= since).OrderBy(a => a.DueAtLocal).ToListAsync(ct);

        // Only the subjects the student takes.
        var visible = new List<Assignment>();
        foreach (var g in rows.GroupBy(a => (a.SectionId, a.SubjectId)))
        {
            var section = await lookups.SectionAsync(g.Key.SectionId, ct);
            if ((await roster.TakersAsync(section.YearGroupId, section.AcademicYearId, g.Key.SubjectId, [studentId], ct)).Contains(studentId)) visible.AddRange(g);
        }
        var ids = visible.Select(a => a.Id).ToList();
        var subs = await db.AssignmentSubmissions.IgnoreQueryFilters().AsNoTracking().Where(s => s.StudentId == studentId && ids.Contains(s.AssignmentId))
            .ToDictionaryAsync(s => s.AssignmentId, ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var now = NowLocal;
        return visible.OrderBy(a => a.DueAtLocal < now).ThenBy(a => a.DueAtLocal).Select(a =>
        {
            var s = subs.GetValueOrDefault(a.Id);
            var canSubmit = a.OnlineSubmission && s?.Status != SubmissionStatus.Graded && (a.AllowLate || now <= a.DueAtLocal || s?.Status == SubmissionStatus.ReturnedForRedo);
            return new StudentHomework(a.Id, subjects.GetValueOrDefault(a.SubjectId).Name, a.Title, a.Instructions, a.SetOn, a.DueAtLocal, a.MaxMarks, a.OnlineSubmission,
                a.AllowLate, a.LatePenaltyPercent, a.AttachmentPath is not null, a.AttachmentName, s is null && now > a.DueAtLocal, s?.Status, s?.SubmittedAtUtc, s?.IsLate ?? false,
                s?.Text, s?.FileName, s?.Status == SubmissionStatus.Graded ? s.FinalMarks : null, s?.Status is SubmissionStatus.Graded or SubmissionStatus.ReturnedForRedo ? s.Feedback : null,
                canSubmit);
        }).ToList();
    }

    public async Task<StudentHomework> SubmitAsync(Guid studentId, Guid assignmentId, SubmissionForm form, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        var a = await db.Assignments.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == assignmentId && !x.IsDeleted, ct) ?? throw new NotFoundException("Homework", assignmentId);
        await EnsurePortalSeesAsync(a, ct, studentId);
        if (!a.OnlineSubmission) throw new DomainException("This homework is handed in at school, not online.", "not_online");
        if (string.IsNullOrWhiteSpace(form.Text) && form.File is not { Length: > 0 }) throw new DomainException("Write an answer or attach a file or photo.", "empty_submission");

        using var _ = db.ElevateToSystem(); // the family's own record on the child's campus
        var s = await db.AssignmentSubmissions.FirstOrDefaultAsync(x => x.AssignmentId == a.Id && x.StudentId == studentId, ct);
        if (s?.Status == SubmissionStatus.Graded) throw new DomainException("This homework has already been marked.", "already_graded", isConflict: true);
        var now = NowLocal;
        var late = now > a.DueAtLocal && s?.Status != SubmissionStatus.ReturnedForRedo;
        if (late && !a.AllowLate) throw new DomainException($"The deadline ({a.DueAtLocal:dd MMM, hh:mm tt}) has passed and late work is not accepted.", "deadline_passed", isConflict: true);

        if (s is null)
        {
            s = new AssignmentSubmission { CampusId = a.CampusId, AssignmentId = a.Id, StudentId = studentId };
            db.AssignmentSubmissions.Add(s);
        }
        else if (s.FilePath is not null && form.File is { Length: > 0 })
        {
            storage.Delete(s.FilePath);
            s.FilePath = s.FileName = s.FileType = null;
        }
        s.Text = LearningText.Clean(form.Text);
        if (form.File is { Length: > 0 } file)
        {
            await using var stream = file.OpenReadStream();
            var stored = await storage.SaveAsync(stream, $"homework/{a.Id:N}", file.FileName, file.ContentType, ct, allowOfficeDocuments: true);
            s.FilePath = stored.Path;
            s.FileName = stored.FileName;
            s.FileType = stored.ContentType;
        }
        s.SubmittedAtUtc = clock.GetUtcNow().UtcDateTime;
        s.SubmittedByUserId = currentUser.UserId;
        s.IsLate = late;
        s.Status = SubmissionStatus.Submitted;
        s.Marks = s.FinalMarks = null;
        await db.SaveChangesAsync(ct);
        return (await ForStudentAsync(studentId, true, ct)).First(h => h.AssignmentId == a.Id);
    }

    // ------------------------------------------------------------------ internals

    public static decimal? FinalMarks(decimal? marks, bool late, decimal penaltyPercent) =>
        marks is not { } m ? null : late && penaltyPercent > 0 ? Math.Round(m * (1 - penaltyPercent / 100m), 2) : m;

    private static void Apply(Assignment a, AssignmentForm form)
    {
        a.Title = form.Title.Trim();
        a.Instructions = LearningText.Clean(form.Instructions);
        a.DueAtLocal = form.DueAtLocal;
        a.MaxMarks = form.MaxMarks;
        a.OnlineSubmission = form.OnlineSubmission;
        a.AllowLate = form.AllowLate;
    }

    private async Task SaveAttachmentAsync(Assignment a, IFormFile? file, CancellationToken ct)
    {
        if (file is not { Length: > 0 }) return;
        await using var stream = file.OpenReadStream();
        var stored = await storage.SaveAsync(stream, $"homework/{a.Id:N}", file.FileName, file.ContentType, ct, allowOfficeDocuments: true);
        a.AttachmentPath = stored.Path;
        a.AttachmentName = stored.FileName;
        a.AttachmentType = stored.ContentType;
    }

    private async Task RecalculateFinalMarksAsync(Assignment a, CancellationToken ct)
    {
        foreach (var s in await db.AssignmentSubmissions.Where(s => s.AssignmentId == a.Id && s.Marks != null).ToListAsync(ct))
            s.FinalMarks = FinalMarks(s.Marks, s.IsLate, a.LatePenaltyPercent);
    }

    private async Task EnsureCanEditAsync(Assignment a, CancellationToken ct)
    {
        if (currentUser.HasPermission(Permissions.Learning.Manage, a.CampusId)) return;
        var section = await lookups.SectionAsync(a.SectionId, ct);
        if (a.PostedByUserId == currentUser.UserId || section.ClassTeacherUserId == currentUser.UserId)
        {
            await access.EnsureAsync(section, a.SubjectId, Permissions.Learning.Teach, Permissions.Learning.Manage, ct);
            return;
        }
        throw new ForbiddenException("Only the teacher who set this homework can change it.");
    }

    /// <summary>A family member may see homework of the sections their child is (or was) in, for subjects the child takes.</summary>
    private async Task EnsurePortalSeesAsync(Assignment a, CancellationToken ct, Guid? studentId = null)
    {
        var mine = studentId is { } one ? [one] : await portal.MyStudentsAsync(ct);
        var inSection = await db.StudentEnrolments.IgnoreQueryFilters().AsNoTracking()
            .Where(e => !e.IsDeleted && e.SectionId == a.SectionId && mine.Contains(e.StudentId)).Select(e => e.StudentId).ToListAsync(ct);
        if (inSection.Count == 0) throw new ForbiddenException("This homework is not for your class.");
        var section = await lookups.SectionAsync(a.SectionId, ct);
        if ((await roster.TakersAsync(section.YearGroupId, section.AcademicYearId, a.SubjectId, inSection, ct)).Count == 0)
            throw new ForbiddenException("This homework is not for your subjects.");
    }

    private async Task<bool> LockedAsync(GradebookItem item, CancellationToken ct) =>
        await db.GradebookModerations.AsNoTracking().AnyAsync(m => m.SectionId == item.SectionId && m.SubjectId == item.SubjectId && m.TermId == item.TermId
                                                                  && m.Status == ModerationStatus.Approved, ct);

    private async Task<List<AssignmentView>> ViewsAsync(IReadOnlyList<Assignment> rows, CancellationToken ct)
    {
        var sections = await lookups.SectionsAsync(rows.Select(r => r.SectionId), ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var names = await lookups.UserNamesAsync(rows.Select(r => r.PostedByUserId), ct);
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.AssignmentSubmissions.AsNoTracking().Where(s => ids.Contains(s.AssignmentId)).GroupBy(s => s.AssignmentId)
            .Select(g => new { g.Key, Submitted = g.Count(s => s.FilePath != null || s.Text != null), Graded = g.Count(s => s.Status == SubmissionStatus.Graded) })
            .ToDictionaryAsync(x => x.Key, ct);
        var inGradebook = (await db.GradebookItems.AsNoTracking().Where(i => i.AssignmentId != null && ids.Contains(i.AssignmentId.Value))
            .Select(i => i.AssignmentId!.Value).ToListAsync(ct)).ToHashSet();
        var today = lookups.TodayDhaka;

        var result = new List<AssignmentView>();
        foreach (var a in rows)
        {
            var section = sections.GetValueOrDefault(a.SectionId);
            var students = section is null ? 0 : (await roster.TakingAsync(section, a.SubjectId, today, ct)).Count;
            var canEdit = currentUser.HasPermission(Permissions.Learning.Manage, a.CampusId)
                          || (currentUser.HasPermission(Permissions.Learning.Teach, a.CampusId) && (a.PostedByUserId == currentUser.UserId || section?.ClassTeacherUserId == currentUser.UserId));
            var c = counts.GetValueOrDefault(a.Id);
            result.Add(new AssignmentView(a.Id, a.CampusId, a.SectionId, section?.Label ?? "?", a.SubjectId, subjects.GetValueOrDefault(a.SubjectId).Name ?? "?", a.Title,
                a.Instructions, a.SetOn, a.DueAtLocal, a.MaxMarks, a.OnlineSubmission, a.AllowLate, a.LatePenaltyPercent, a.AttachmentPath is not null, a.AttachmentName,
                a.PostedByUserId is { } p ? names.GetValueOrDefault(p) : null, students, c?.Submitted ?? 0, c?.Graded ?? 0, inGradebook.Contains(a.Id), canEdit));
        }
        return result;
    }
}

/// <summary>Appendix C "homework due tomorrow": after the reminder time, tells students and guardians who have not handed in yet (once per homework).</summary>
public sealed class HomeworkReminderJob : IScheduledJob
{
    public string Name => "homework-reminders";
    public TimeSpan Interval => TimeSpan.FromMinutes(30);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var lookups = services.GetRequiredService<AcademicLookups>();
        var roster = services.GetRequiredService<ClassRoster>();
        var settings = services.GetRequiredService<ISettingsService>();
        var notifier = services.GetRequiredService<GuardianNotifier>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var today = lookups.TodayDhaka;
        var tomorrow = today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var timeNow = TimeOnly.FromDateTime(BanglaFormat.ToDhaka(now));

        var due = await db.Assignments.Where(a => a.OnlineSubmission && a.ReminderSentAtUtc == null && a.DueAtLocal >= tomorrow && a.DueAtLocal < tomorrow.AddDays(1))
            .ToListAsync(ct);
        if (due.Count == 0) return;
        var subjects = await lookups.SubjectsAsync(ct);
        foreach (var a in due)
        {
            if (timeNow < await settings.GetTimeAsync(SettingKeys.HomeworkReminderTime, new TimeOnly(16, 0), a.CampusId, ct)) continue;
            var section = await lookups.SectionAsync(a.SectionId, ct);
            var handedIn = (await db.AssignmentSubmissions.AsNoTracking().Where(s => s.AssignmentId == a.Id).Select(s => s.StudentId).ToListAsync(ct)).ToHashSet();
            foreach (var s in (await roster.TakingAsync(section, a.SubjectId, today, ct)).Where(s => !handedIn.Contains(s.StudentId)))
                await notifier.NotifyAsync(s.StudentId, a.CampusId, TemplateCodes.HomeworkDue, new Dictionary<string, string?>
                {
                    ["ChildName"] = s.Name, ["Subject"] = subjects.GetValueOrDefault(a.SubjectId).Name, ["Title"] = a.Title, ["Due"] = a.DueAtLocal.ToString("dd MMM, hh:mm tt")
                }, nameof(Assignment), a.Id.ToString(), ct, sms: false, includeStudent: true);
            a.ReminderSentAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
    }
}
