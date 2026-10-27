using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Api.Modules.Exams;
using SchoolManagement.Domain.Learning;

namespace SchoolManagement.Api.Modules.Learning;

public record CategoryView(Guid Id, Guid? YearGroupId, string Name, decimal WeightPercent, int Sequence);

public record CategoryRequest(string Name, decimal WeightPercent);

public record CategoriesRequest(Guid? YearGroupId, List<CategoryRequest> Categories);

public record GradebookItemRequest(Guid SectionId, Guid SubjectId, Guid TermId, Guid CategoryId, string Title, DateOnly Date, decimal MaxMarks);

public record GradebookItemView(Guid Id, Guid CategoryId, string CategoryName, string Title, DateOnly Date, decimal MaxMarks, Guid? AssignmentId);

public record MarkCell(decimal? Marks, bool Excused, string? Comment);

public record GradebookStudentRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, IReadOnlyDictionary<Guid, MarkCell> Marks,
    IReadOnlyDictionary<Guid, decimal> CategoryPercents, decimal? TermPercent, decimal? ModeratedPercent, string? Grade);

public record ModerationView(ModerationStatus Status, decimal Adjustment, string? SubmittedBy, DateTime? SubmittedAtUtc, string? ModeratedBy, DateTime? ModeratedAtUtc, string? Note);

public record GradebookView(Guid SectionId, string SectionLabel, Guid SubjectId, string SubjectName, Guid TermId, string TermName, IReadOnlyList<CategoryView> Categories,
    IReadOnlyList<GradebookItemView> Items, IReadOnlyList<GradebookStudentRow> Students, ModerationView Moderation, decimal? ClassAverage, bool CanEdit, bool CanModerate);

public record MarkEntryRequest(Guid ItemId, Guid StudentId, decimal? Marks, bool Excused, string? Comment);

public record SaveGradebookMarksRequest(List<MarkEntryRequest> Entries);

public record ModerationDecision(bool Approve, decimal Adjustment, string? Note);

public record ModerationQueueRow(Guid SectionId, Guid CampusId, string SectionLabel, Guid SubjectId, string SubjectName, Guid TermId, string TermName, ModerationStatus Status,
    string? SubmittedBy, DateTime? SubmittedAtUtc, int Items, decimal? ClassAverage);

/// <summary>
/// LRN-08: continuous assessment. Classwork, homework, quizzes, projects and practicals are weighted by category into a
/// term %, moderated (and optionally adjusted) by the head of department; approval locks the gradebook and makes the
/// moderated % available to exam report cards as the CA component.
/// </summary>
public sealed class GradebookService(
    AppDbContext db,
    AcademicLookups lookups,
    ClassRoster roster,
    TeachingAccess access,
    Grading grading,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    // ------------------------------------------------------------------ categories

    /// <summary>The categories in force for a class: its own set when it has one, otherwise the school defaults.</summary>
    public async Task<List<CategoryView>> CategoriesAsync(Guid? yearGroupId, CancellationToken ct)
    {
        var rows = await db.GradebookCategories.AsNoTracking().OrderBy(c => c.Sequence).ToListAsync(ct);
        var own = yearGroupId is null ? [] : rows.Where(c => c.YearGroupId == yearGroupId).ToList();
        return (own.Count > 0 ? own : rows.Where(c => c.YearGroupId == null))
            .Select(c => new CategoryView(c.Id, c.YearGroupId, c.Name, c.WeightPercent, c.Sequence)).ToList();
    }

    /// <summary>Replaces the category set (school default when YearGroupId is null). Categories already used keep their id.</summary>
    public async Task<List<CategoryView>> SaveCategoriesAsync(CategoriesRequest r, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Learning.Manage);
        if (r.YearGroupId is null && r.Categories.Count == 0) throw new DomainException("The school default needs at least one category.", "no_categories");
        if (r.Categories.Count > 0 && r.Categories.Sum(c => c.WeightPercent) != 100) throw new DomainException("The weights must add up to 100%.", "weights_not_100");
        if (r.Categories.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new DomainException("Category names must differ.", "duplicate_category");

        var existing = await db.GradebookCategories.Where(c => c.YearGroupId == r.YearGroupId).ToListAsync(ct);
        var seq = 0;
        foreach (var c in r.Categories)
        {
            var row = existing.FirstOrDefault(e => e.Name.Equals(c.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new GradebookCategory { YearGroupId = r.YearGroupId, Name = c.Name.Trim() };
                db.GradebookCategories.Add(row);
            }
            row.WeightPercent = c.WeightPercent;
            row.Sequence = seq++;
        }
        foreach (var gone in existing.Where(e => r.Categories.All(c => !c.Name.Trim().Equals(e.Name, StringComparison.OrdinalIgnoreCase))))
        {
            if (await db.GradebookItems.AnyAsync(i => i.CategoryId == gone.Id, ct))
                throw new DomainException($"'{gone.Name}' is used by gradebook items and cannot be removed; set its weight to 0 instead.", "category_in_use", isConflict: true);
            db.GradebookCategories.Remove(gone);
        }
        await db.SaveChangesAsync(ct);
        return await CategoriesAsync(r.YearGroupId, ct);
    }

    // ------------------------------------------------------------------ the gradebook

    public async Task<GradebookView> GetAsync(Guid sectionId, Guid subjectId, Guid termId, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        access.EnsureCampus(section.CampusId);
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term", termId);
        var categories = await CategoriesForItemsAsync(section, subjectId, termId, ct);
        var items = await db.GradebookItems.AsNoTracking().Where(i => i.SectionId == sectionId && i.SubjectId == subjectId && i.TermId == termId)
            .OrderBy(i => i.Date).ThenBy(i => i.CreatedAtUtc).ToListAsync(ct);
        var students = await roster.TakingAsync(section, subjectId, term.EndDate < lookups.TodayDhaka ? term.EndDate : lookups.TodayDhaka, ct);
        var cells = await CellsAsync(items, ct);
        var moderation = await db.GradebookModerations.AsNoTracking().FirstOrDefaultAsync(m => m.SectionId == sectionId && m.SubjectId == subjectId && m.TermId == termId, ct);
        var adjustment = moderation?.Status == ModerationStatus.Approved ? moderation.Adjustment : 0;
        var scale = await grading.ForYearGroupAsync(section.YearGroupId, null, ct);

        var rows = students.Select(s =>
        {
            var mine = cells.GetValueOrDefault(s.StudentId) ?? [];
            var (byCategory, total) = Calculate(categories, items, mine);
            decimal? moderated = total is { } t ? Math.Clamp(t + adjustment, 0, 100) : null;
            return new GradebookStudentRow(s.StudentId, s.AdmissionNo, s.Name, s.RollNo, mine, byCategory, total, moderated,
                moderated is { } m ? scale?.BandFor(m)?.Grade : null);
        }).ToList();

        var names = await lookups.UserNamesAsync([moderation?.SubmittedByUserId, moderation?.ModeratedByUserId], ct);
        var subject = (await lookups.SubjectsAsync(ct)).GetValueOrDefault(subjectId).Name ?? "?";
        var withPercent = rows.Where(r => r.ModeratedPercent is not null).ToList();
        return new GradebookView(section.Id, section.Label, subjectId, subject, term.Id, term.Name, categories,
            items.Select(i => new GradebookItemView(i.Id, i.CategoryId, categories.FirstOrDefault(c => c.Id == i.CategoryId)?.Name ?? "?", i.Title, i.Date, i.MaxMarks, i.AssignmentId)).ToList(),
            rows,
            new ModerationView(moderation?.Status ?? ModerationStatus.Open, moderation?.Adjustment ?? 0,
                moderation?.SubmittedByUserId is { } sb ? names.GetValueOrDefault(sb) : null, moderation?.SubmittedAtUtc,
                moderation?.ModeratedByUserId is { } mb ? names.GetValueOrDefault(mb) : null, moderation?.ModeratedAtUtc, moderation?.Note),
            withPercent.Count == 0 ? null : Math.Round(withPercent.Average(r => r.ModeratedPercent!.Value), 1),
            moderation?.Status is not (ModerationStatus.Submitted or ModerationStatus.Approved)
            && await access.CanAsync(section, subjectId, Permissions.Learning.Teach, Permissions.Learning.Manage, ct),
            currentUser.HasPermission(Permissions.Learning.Moderate, section.CampusId));
    }

    public async Task<GradebookItemView> AddItemAsync(GradebookItemRequest r, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(r.SectionId, ct);
        await EnsureEditableAsync(section, r.SubjectId, r.TermId, ct);
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.TermId && t.AcademicYearId == section.AcademicYearId, ct)
                   ?? throw new DomainException("The term does not belong to this class's academic year.", "wrong_term");
        if (r.Date < term.StartDate || r.Date > term.EndDate) throw new DomainException($"The date must fall in {term.Name}.", "date_outside_term");
        var category = (await CategoriesAsync(section.YearGroupId, ct)).FirstOrDefault(c => c.Id == r.CategoryId)
                       ?? throw new DomainException("Choose one of this class's gradebook categories.", "bad_category");
        var item = new GradebookItem
        {
            CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, SectionId = section.Id, SubjectId = r.SubjectId, TermId = term.Id,
            CategoryId = category.Id, Title = r.Title.Trim(), Date = r.Date, MaxMarks = r.MaxMarks
        };
        db.GradebookItems.Add(item);
        await db.SaveChangesAsync(ct);
        return new GradebookItemView(item.Id, item.CategoryId, category.Name, item.Title, item.Date, item.MaxMarks, null);
    }

    public async Task DeleteItemAsync(Guid itemId, CancellationToken ct)
    {
        var item = await db.GradebookItems.FirstOrDefaultAsync(i => i.Id == itemId, ct) ?? throw new NotFoundException("Gradebook item", itemId);
        if (item.AssignmentId is not null) throw new DomainException("This column comes from homework; delete the homework instead.", "from_homework");
        await EnsureEditableAsync(await lookups.SectionAsync(item.SectionId, ct), item.SubjectId, item.TermId, ct);
        db.GradebookMarks.RemoveRange(await db.GradebookMarks.Where(m => m.ItemId == itemId).ToListAsync(ct));
        db.GradebookItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task<GradebookView> SaveMarksAsync(Guid sectionId, Guid subjectId, Guid termId, SaveGradebookMarksRequest r, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        await EnsureEditableAsync(section, subjectId, termId, ct);
        var items = await db.GradebookItems.AsNoTracking().Where(i => i.SectionId == sectionId && i.SubjectId == subjectId && i.TermId == termId).ToDictionaryAsync(i => i.Id, ct);
        var studentIds = r.Entries.Select(e => e.StudentId).Distinct().ToList();
        var takers = await roster.TakersAsync(section.YearGroupId, section.AcademicYearId, subjectId, studentIds, ct);
        var itemIds = items.Keys.ToList();
        var existing = await db.GradebookMarks.Where(m => itemIds.Contains(m.ItemId) && studentIds.Contains(m.StudentId)).ToDictionaryAsync(m => (m.ItemId, m.StudentId), ct);

        foreach (var e in r.Entries)
        {
            if (!items.TryGetValue(e.ItemId, out var item)) throw new DomainException("A column in the request is not in this gradebook.", "bad_item");
            if (item.AssignmentId is not null) continue; // homework marks are given on the homework page
            if (!takers.Contains(e.StudentId)) throw new DomainException("A student in the request does not take this subject in the class.", "not_in_class");
            if (e.Marks is { } m && (m < 0 || m > item.MaxMarks)) throw new DomainException($"Marks for '{item.Title}' must be between 0 and {item.MaxMarks:0.##}.", "marks_out_of_range");
            if (!existing.TryGetValue((e.ItemId, e.StudentId), out var row))
            {
                if (e.Marks is null && !e.Excused && string.IsNullOrWhiteSpace(e.Comment)) continue;
                row = new GradebookMark { CampusId = section.CampusId, ItemId = e.ItemId, StudentId = e.StudentId };
                db.GradebookMarks.Add(row);
            }
            row.Marks = e.Excused ? null : e.Marks;
            row.Excused = e.Excused;
            row.Comment = LearningText.Clean(e.Comment);
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(sectionId, subjectId, termId, ct);
    }

    // ------------------------------------------------------------------ moderation

    public async Task<GradebookView> SubmitAsync(Guid sectionId, Guid subjectId, Guid termId, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        await EnsureEditableAsync(section, subjectId, termId, ct);
        if (!await db.GradebookItems.AnyAsync(i => i.SectionId == sectionId && i.SubjectId == subjectId && i.TermId == termId, ct))
            throw new DomainException("Add assessed work before submitting the gradebook.", "empty_gradebook");
        var m = await ModerationRowAsync(section, subjectId, termId, ct);
        m.Status = ModerationStatus.Submitted;
        m.SubmittedByUserId = currentUser.UserId;
        m.SubmittedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return await GetAsync(sectionId, subjectId, termId, ct);
    }

    public async Task<GradebookView> ModerateAsync(Guid sectionId, Guid subjectId, Guid termId, ModerationDecision r, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        currentUser.EnsurePermission(Permissions.Learning.Moderate, section.CampusId);
        var m = await db.GradebookModerations.FirstOrDefaultAsync(x => x.SectionId == sectionId && x.SubjectId == subjectId && x.TermId == termId, ct);
        if (m?.Status != ModerationStatus.Submitted) throw new DomainException("Only a submitted gradebook can be moderated.", "not_submitted", isConflict: true);
        if (!r.Approve && string.IsNullOrWhiteSpace(r.Note)) throw new DomainException("Tell the teacher what to change.", "note_required");
        m.Status = r.Approve ? ModerationStatus.Approved : ModerationStatus.Returned;
        m.Adjustment = r.Approve ? r.Adjustment : 0;
        m.Note = LearningText.Clean(r.Note);
        m.ModeratedByUserId = currentUser.UserId;
        m.ModeratedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return await GetAsync(sectionId, subjectId, termId, ct);
    }

    /// <summary>Unlocks an approved gradebook for corrections; it must be submitted and approved again.</summary>
    public async Task<GradebookView> ReopenAsync(Guid sectionId, Guid subjectId, Guid termId, string reason, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        currentUser.EnsurePermission(Permissions.Learning.Moderate, section.CampusId);
        var m = await db.GradebookModerations.FirstOrDefaultAsync(x => x.SectionId == sectionId && x.SubjectId == subjectId && x.TermId == termId, ct);
        if (m?.Status != ModerationStatus.Approved) throw new DomainException("Only an approved gradebook can be re-opened.", "not_approved", isConflict: true);
        m.Status = ModerationStatus.Returned;
        m.Note = $"Re-opened: {reason.Trim()}";
        m.ModeratedByUserId = currentUser.UserId;
        m.ModeratedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return await GetAsync(sectionId, subjectId, termId, ct);
    }

    public async Task<List<ModerationQueueRow>> QueueAsync(ModerationStatus? status, Guid? termId, CancellationToken ct)
    {
        var q = db.GradebookModerations.AsNoTracking();
        q = status is { } s ? q.Where(m => m.Status == s) : q.Where(m => m.Status != ModerationStatus.Open);
        if (termId is not null) q = q.Where(m => m.TermId == termId);
        var rows = await q.OrderBy(m => m.Status).ThenByDescending(m => m.SubmittedAtUtc).Take(300).ToListAsync(ct);
        var sections = await lookups.SectionsAsync(rows.Select(r => r.SectionId), ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var terms = await db.Terms.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var names = await lookups.UserNamesAsync(rows.Select(r => r.SubmittedByUserId), ct);
        var result = new List<ModerationQueueRow>();
        foreach (var m in rows)
        {
            var view = await GetAsync(m.SectionId, m.SubjectId, m.TermId, ct);
            result.Add(new ModerationQueueRow(m.SectionId, m.CampusId, sections.GetValueOrDefault(m.SectionId)?.Label ?? "?", m.SubjectId, subjects.GetValueOrDefault(m.SubjectId).Name ?? "?",
                m.TermId, terms.GetValueOrDefault(m.TermId) ?? "?", m.Status, m.SubmittedByUserId is { } u ? names.GetValueOrDefault(u) : null, m.SubmittedAtUtc,
                view.Items.Count, view.ClassAverage));
        }
        return result;
    }

    /// <summary>
    /// The moderated term % per student, for the exam CA component (EXM-03). Null when the gradebook has not been approved.
    /// </summary>
    public async Task<Dictionary<Guid, decimal>?> ApprovedPercentsAsync(Guid sectionId, Guid subjectId, Guid termId, CancellationToken ct)
    {
        if (!await db.GradebookModerations.AnyAsync(m => m.SectionId == sectionId && m.SubjectId == subjectId && m.TermId == termId && m.Status == ModerationStatus.Approved, ct))
            return null;
        var view = await GetAsync(sectionId, subjectId, termId, ct);
        return view.Students.Where(s => s.ModeratedPercent is not null).ToDictionary(s => s.StudentId, s => s.ModeratedPercent!.Value);
    }

    // ------------------------------------------------------------------ calculation

    /// <summary>
    /// Term % = Σ(weight × category %) / Σ(weight) over the categories that have marked work for the student. A category %
    /// is the marks over the maximum of its marked (not excused) items; unmarked work is not counted until it is marked.
    /// </summary>
    public static (Dictionary<Guid, decimal> ByCategory, decimal? Total) Calculate(IReadOnlyList<CategoryView> categories, IReadOnlyList<GradebookItem> items,
        IReadOnlyDictionary<Guid, MarkCell> marks)
    {
        var byCategory = new Dictionary<Guid, decimal>();
        decimal weighted = 0, weights = 0;
        foreach (var c in categories)
        {
            decimal got = 0, max = 0;
            foreach (var i in items.Where(i => i.CategoryId == c.Id))
                if (marks.TryGetValue(i.Id, out var cell) && cell is { Excused: false, Marks: { } m } && i.MaxMarks > 0)
                {
                    got += m;
                    max += i.MaxMarks;
                }
            if (max == 0) continue;
            var pct = Math.Round(got * 100 / max, 2);
            byCategory[c.Id] = pct;
            if (c.WeightPercent <= 0) continue;
            weighted += pct * c.WeightPercent;
            weights += c.WeightPercent;
        }
        return (byCategory, weights == 0 ? null : Math.Round(weighted / weights, 1));
    }

    private async Task<Dictionary<Guid, Dictionary<Guid, MarkCell>>> CellsAsync(IReadOnlyList<GradebookItem> items, CancellationToken ct)
    {
        var result = new Dictionary<Guid, Dictionary<Guid, MarkCell>>();
        void Put(Guid student, Guid item, MarkCell cell)
        {
            if (!result.TryGetValue(student, out var row)) result[student] = row = [];
            row[item] = cell;
        }
        var plain = items.Where(i => i.AssignmentId is null).Select(i => i.Id).ToList();
        foreach (var m in await db.GradebookMarks.AsNoTracking().Where(m => plain.Contains(m.ItemId)).ToListAsync(ct))
            Put(m.StudentId, m.ItemId, new MarkCell(m.Marks, m.Excused, m.Comment));
        var homework = items.Where(i => i.AssignmentId is not null).ToDictionary(i => i.AssignmentId!.Value, i => i.Id);
        var assignmentIds = homework.Keys.ToList();
        foreach (var s in await db.AssignmentSubmissions.AsNoTracking().Where(s => assignmentIds.Contains(s.AssignmentId) && s.Status == SubmissionStatus.Graded).ToListAsync(ct))
            Put(s.StudentId, homework[s.AssignmentId], new MarkCell(s.FinalMarks, false, s.Feedback));
        return result;
    }

    /// <summary>The class's categories plus any others its existing items already use (e.g. after the weights changed).</summary>
    private async Task<List<CategoryView>> CategoriesForItemsAsync(SectionInfo section, Guid subjectId, Guid termId, CancellationToken ct)
    {
        var categories = await CategoriesAsync(section.YearGroupId, ct);
        var used = await db.GradebookItems.AsNoTracking().Where(i => i.SectionId == section.Id && i.SubjectId == subjectId && i.TermId == termId)
            .Select(i => i.CategoryId).Distinct().ToListAsync(ct);
        var missing = used.Where(id => categories.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
            categories.AddRange(await db.GradebookCategories.IgnoreQueryFilters().AsNoTracking().Where(c => missing.Contains(c.Id))
                .Select(c => new CategoryView(c.Id, c.YearGroupId, c.Name, c.IsDeleted ? 0 : c.WeightPercent, c.Sequence)).ToListAsync(ct));
        return categories;
    }

    private async Task EnsureEditableAsync(SectionInfo section, Guid subjectId, Guid termId, CancellationToken ct)
    {
        await access.EnsureAsync(section, subjectId, Permissions.Learning.Teach, Permissions.Learning.Manage, ct);
        var status = await db.GradebookModerations.AsNoTracking().Where(m => m.SectionId == section.Id && m.SubjectId == subjectId && m.TermId == termId)
            .Select(m => (ModerationStatus?)m.Status).FirstOrDefaultAsync(ct);
        if (status == ModerationStatus.Approved) throw new DomainException("This gradebook is approved and locked; ask the moderator to re-open it.", "gradebook_locked", isConflict: true);
        if (status == ModerationStatus.Submitted) throw new DomainException("This gradebook is with the moderator; it can be changed if it is returned.", "gradebook_submitted", isConflict: true);
    }

    private async Task<GradebookModeration> ModerationRowAsync(SectionInfo section, Guid subjectId, Guid termId, CancellationToken ct)
    {
        var m = await db.GradebookModerations.FirstOrDefaultAsync(x => x.SectionId == section.Id && x.SubjectId == subjectId && x.TermId == termId, ct);
        if (m is not null) return m;
        m = new GradebookModeration { CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, SectionId = section.Id, SubjectId = subjectId, TermId = termId };
        db.GradebookModerations.Add(m);
        return m;
    }
}
