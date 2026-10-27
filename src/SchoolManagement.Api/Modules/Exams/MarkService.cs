using System.Globalization;
using ClosedXML.Excel;
using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Api.Modules.Learning;
using SchoolManagement.Domain.Exams;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Exams;

public record MarkSheetSummary(Guid PaperId, Guid SectionId, string SectionLabel, Guid CampusId, string ClassName, string SubjectName, DateOnly Date, MarkSheetStatus Status,
    int Candidates, int Complete, string? EnteredBy, string? VerifiedBy, string? ApprovedBy, bool CanEnter);

public record MyMarkSheet(Guid ExamId, string ExamName, MarkSheetSummary Sheet);

public record MarkCellView(Guid ComponentId, decimal? Marks, MarkCode Code, decimal? SecondMarks, MarkCode? SecondCode, bool Mismatch);

public record MarkRowView(Guid StudentId, string AdmissionNo, string Name, int? RollNo, IReadOnlyList<MarkCellView> Cells, decimal? Percent, string? Grade, string? Status);

public record MarkSheetView(Guid PaperId, Guid SectionId, string SectionLabel, string ExamName, string ClassName, string SubjectName, DateOnly Date,
    IReadOnlyList<ComponentView> Components, MarkSheetStatus Status, bool DoubleEntry, string? EnteredBy, DateTime? SubmittedAtUtc, string? VerifiedBy,
    string? ApprovedBy, DateTime? ApprovedAtUtc, int ReopenCount, string? ReopenReason, int Mismatches, bool CanEnter, bool CanVerify, bool CanApprove,
    bool CanReopen, IReadOnlyList<MarkRowView> Students);

public record MarkInput(Guid StudentId, Guid ComponentId, decimal? Marks, MarkCode Code);

public record SaveMarksRequest(List<MarkInput> Entries, bool SecondEntry = false);

public record MarkImportRow(int Row, string AdmissionNo, string? Name, IReadOnlyList<string> Errors);

public record MarkImportResult(bool DryRun, int Rows, int Valid, bool Saved, IReadOnlyList<MarkImportRow> Problems);

/// <summary>A component's marks for the subject % (see <see cref="MarkService.SubjectPercent"/>).</summary>
public record ComponentScore(decimal MaxMarks, decimal WeightPercent, decimal? Marks, MarkCode Code);

/// <summary>
/// EXM-03: per-subject mark entry by section, on screen or by Excel upload, with component weights and absent/exempt codes.
/// With double entry on, a second person re-enters the marks and the sheet is verified only when both entries agree.
/// The moderator approves (locks) the sheet and can re-open it with a reason.
/// </summary>
public sealed class MarkService(
    AppDbContext db,
    AcademicLookups lookups,
    ExamService exams,
    TeachingAccess access,
    GradebookService gradebook,
    Grading grading,
    ISettingsService settings,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<List<MarkSheetSummary>> SheetsAsync(Guid examId, CancellationToken ct)
    {
        var exam = await exams.LoadAsync(examId, ct);
        var candidates = await exams.CandidatesAsync(exam, ct);
        var papers = await db.ExamPapers.AsNoTracking().Include(p => p.Components).Where(p => p.ExamId == examId).ToListAsync(ct);
        var paperIds = papers.Select(p => p.Id).ToList();
        var sheets = await db.MarkSheets.AsNoTracking().Where(s => paperIds.Contains(s.ExamPaperId)).ToDictionaryAsync(s => (s.ExamPaperId, s.SectionId), ct);
        var counts = await db.ExamMarks.AsNoTracking().Where(m => paperIds.Contains(m.ExamPaperId) && (m.Marks != null || m.Code != MarkCode.None))
            .GroupBy(m => new { m.ExamPaperId, m.StudentId }).Select(g => new { g.Key.ExamPaperId, g.Key.StudentId, Count = g.Count() }).ToListAsync(ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var classes = await exams.ClassNamesAsync(ct);
        var names = await lookups.UserNamesAsync(sheets.Values.SelectMany(s => new[] { s.EnteredByUserId, s.VerifiedByUserId, s.ApprovedByUserId }), ct);

        var result = new List<MarkSheetSummary>();
        foreach (var p in papers)
            foreach (var group in candidates.Where(c => c.PaperIds.Contains(p.Id)).GroupBy(c => c.Section.Id))
            {
                var section = group.First().Section;
                var s = sheets.GetValueOrDefault((p.Id, section.Id));
                var ids = group.Select(c => c.StudentId).ToHashSet();
                var typed = p.Components.Count(c => !c.FromGradebook);
                var complete = typed == 0 ? ids.Count : counts.Count(x => x.ExamPaperId == p.Id && ids.Contains(x.StudentId) && x.Count >= typed);
                result.Add(new MarkSheetSummary(p.Id, section.Id, section.Label, section.CampusId, classes.GetValueOrDefault(p.YearGroupId, "?"),
                    subjects.GetValueOrDefault(p.SubjectId).Name ?? "?", p.Date, s?.Status ?? MarkSheetStatus.Draft, ids.Count, Math.Min(complete, ids.Count),
                    Name(s?.EnteredByUserId), Name(s?.VerifiedByUserId), Name(s?.ApprovedByUserId),
                    await access.CanAsync(section, p.SubjectId, Permissions.Exams.Marks, Permissions.Exams.Moderate, ct, subjectOnly: true)));
            }
        return result.OrderBy(r => r.Date).ThenBy(r => r.ClassName).ThenBy(r => r.SubjectName).ThenBy(r => r.SectionLabel).ToList();

        string? Name(Guid? id) => id is { } x ? names.GetValueOrDefault(x) : null;
    }

    /// <summary>Open mark sheets the caller can work on (their own subjects; every sheet for moderators), across exams still taking marks.</summary>
    public async Task<List<MyMarkSheet>> MineAsync(CancellationToken ct)
    {
        var open = await db.Exams.AsNoTracking().Where(e => e.ResultStatus == ResultStatus.NotReady).OrderBy(e => e.StartDate).ToListAsync(ct);
        var result = new List<MyMarkSheet>();
        foreach (var exam in open.Where(e => e.CampusId is null || currentUser.CanAccessCampus(e.CampusId.Value)))
            result.AddRange((await SheetsAsync(exam.Id, ct)).Where(s => s.CanEnter && s.Status != MarkSheetStatus.Approved).Select(s => new MyMarkSheet(exam.Id, exam.Name, s)));
        return result;
    }

    public async Task<MarkSheetView> GetAsync(Guid paperId, Guid sectionId, CancellationToken ct)
    {
        var (exam, paper, section, students) = await ContextAsync(paperId, sectionId, ct);
        var sheet = await db.MarkSheets.AsNoTracking().FirstOrDefaultAsync(s => s.ExamPaperId == paperId && s.SectionId == sectionId, ct);
        var ids = students.Select(s => s.StudentId).ToList();
        var marks = await db.ExamMarks.AsNoTracking().Where(m => m.ExamPaperId == paperId && ids.Contains(m.StudentId)).ToListAsync(ct);
        var components = paper.Components.OrderBy(c => c.Sequence).ToList();
        var scheme = await SchemeAsync(exam, paper.YearGroupId, ct);
        var doubleEntry = await settings.GetBoolAsync(SettingKeys.ExamDoubleEntry, true, section.CampusId, ct);
        var status = sheet?.Status ?? MarkSheetStatus.Draft;

        var rows = students.Select(s =>
        {
            var cells = components.Select(c =>
            {
                var m = marks.FirstOrDefault(x => x.StudentId == s.StudentId && x.ComponentId == c.Id);
                var mismatch = doubleEntry && !c.FromGradebook && status == MarkSheetStatus.Submitted && m?.SecondCode is not null
                               && (m.SecondCode != m.Code || m.SecondMarks != m.Marks);
                return new MarkCellView(c.Id, m?.Marks, m?.Code ?? MarkCode.None, m?.SecondMarks, m?.SecondCode, mismatch);
            }).ToList();
            var (pct, code) = SubjectPercent(components.Select(c => (c, cells.First(x => x.ComponentId == c.Id)))
                .Select(x => new ComponentScore(x.c.MaxMarks, x.c.WeightPercent, x.Item2.Marks, x.Item2.Code)).ToList());
            return new MarkRowView(s.StudentId, s.AdmissionNo, s.Name, s.RollNo, cells, pct, pct is { } p && code != "EX" ? scheme?.BandFor(p)?.Grade : null, code);
        }).ToList();

        var names = await lookups.UserNamesAsync([sheet?.EnteredByUserId, sheet?.VerifiedByUserId, sheet?.ApprovedByUserId], ct);
        var canEnter = await CanEnterAsync(exam, section, paper.SubjectId, status, ct);
        var moderator = currentUser.HasPermission(Permissions.Exams.Moderate, section.CampusId);
        var canVerify = doubleEntry && status == MarkSheetStatus.Submitted && sheet?.EnteredByUserId != currentUser.UserId
                        && (moderator || await access.CanAsync(section, paper.SubjectId, Permissions.Exams.Marks, Permissions.Exams.Moderate, ct, subjectOnly: true));
        var subject = (await lookups.SubjectsAsync(ct)).GetValueOrDefault(paper.SubjectId).Name ?? "?";
        return new MarkSheetView(paper.Id, section.Id, section.Label, exam.Name, (await exams.ClassNamesAsync(ct)).GetValueOrDefault(paper.YearGroupId, "?"), subject, paper.Date,
            components.Select(c => new ComponentView(c.Id, c.Name, c.MaxMarks, c.WeightPercent, c.Sequence, c.FromGradebook)).ToList(), status, doubleEntry,
            Name(sheet?.EnteredByUserId), sheet?.SubmittedAtUtc, Name(sheet?.VerifiedByUserId), Name(sheet?.ApprovedByUserId), sheet?.ApprovedAtUtc,
            sheet?.ReopenCount ?? 0, sheet?.ReopenReason, rows.Sum(r => r.Cells.Count(c => c.Mismatch)), canEnter, canVerify,
            // Without double entry a submitted sheet is already Verified, so both cases approve from Verified.
            moderator && status == MarkSheetStatus.Verified && exam.ResultStatus == ResultStatus.NotReady,
            moderator && status != MarkSheetStatus.Draft && exam.ResultStatus == ResultStatus.NotReady, rows);

        string? Name(Guid? id) => id is { } x ? names.GetValueOrDefault(x) : null;
    }

    public async Task<MarkSheetView> SaveAsync(Guid paperId, Guid sectionId, SaveMarksRequest r, CancellationToken ct)
    {
        var (exam, paper, section, students) = await ContextAsync(paperId, sectionId, ct);
        var sheet = await SheetRowAsync(paper, section, ct);
        var moderator = currentUser.HasPermission(Permissions.Exams.Moderate, section.CampusId);
        if (r.SecondEntry)
        {
            if (sheet.Status != MarkSheetStatus.Submitted) throw new DomainException("The verification entry is made after the first entry is submitted.", "not_submitted", isConflict: true);
            if (sheet.EnteredByUserId == currentUser.UserId) throw new ForbiddenException("The verification entry must be made by someone other than the first enterer.");
            if (!moderator) await access.EnsureAsync(section, paper.SubjectId, Permissions.Exams.Marks, Permissions.Exams.Moderate, ct, subjectOnly: true);
        }
        else if (!await CanEnterAsync(exam, section, paper.SubjectId, sheet.Status, ct))
            throw sheet.Status == MarkSheetStatus.Approved || exam.ResultStatus != ResultStatus.NotReady
                ? new DomainException("This mark sheet is locked; ask the moderator to re-open it.", "sheet_locked", isConflict: true)
                : new ForbiddenException(sheet.Status == MarkSheetStatus.Draft
                    ? "Only the subject teacher of this class (or a moderator) can enter these marks."
                    : "This sheet has been submitted; only a moderator can change it now.");

        await WriteAsync(paper, section, students, sheet, r.Entries, r.SecondEntry, moderator && !r.SecondEntry && sheet.Status != MarkSheetStatus.Draft, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(paperId, sectionId, ct);
    }

    /// <summary>Fills components marked "from gradebook" with the approved LRN-08 term % scaled to the component maximum.</summary>
    public async Task<MarkSheetView> PullGradebookAsync(Guid paperId, Guid sectionId, CancellationToken ct)
    {
        var (exam, paper, section, students) = await ContextAsync(paperId, sectionId, ct);
        var sheet = await SheetRowAsync(paper, section, ct);
        if (!await CanEnterAsync(exam, section, paper.SubjectId, sheet.Status, ct)) throw new ForbiddenException("You cannot change this mark sheet.");
        var linked = paper.Components.Where(c => c.FromGradebook).ToList();
        if (linked.Count == 0) throw new DomainException("No component of this paper comes from the gradebook.", "no_gradebook_component");
        if (exam.TermId is not { } termId) throw new DomainException("Link the exam to a term to take marks from the gradebook.", "no_term");
        var percents = await gradebook.ApprovedPercentsAsync(section.Id, paper.SubjectId, termId, ct)
                       ?? throw new DomainException("The gradebook for this class, subject and term has not been approved by the moderator yet.", "gradebook_not_approved", isConflict: true);
        var ids = students.Select(s => s.StudentId).ToList();
        var existing = await db.ExamMarks.Where(m => m.ExamPaperId == paperId && ids.Contains(m.StudentId)).ToListAsync(ct);
        foreach (var c in linked)
            foreach (var s in students)
            {
                var row = existing.FirstOrDefault(m => m.StudentId == s.StudentId && m.ComponentId == c.Id);
                if (row is null)
                {
                    row = new ExamMark { CampusId = section.CampusId, ExamPaperId = paperId, ComponentId = c.Id, StudentId = s.StudentId };
                    db.ExamMarks.Add(row);
                }
                row.Code = MarkCode.None;
                row.Marks = percents.TryGetValue(s.StudentId, out var pct) ? Math.Round(pct * c.MaxMarks / 100m, 2) : null;
                row.SecondMarks = row.Marks;
                row.SecondCode = row.Code;
            }
        await db.SaveChangesAsync(ct);
        return await GetAsync(paperId, sectionId, ct);
    }

    /// <summary>First entry complete: every student has marks or a code in every component.</summary>
    public async Task<MarkSheetView> SubmitAsync(Guid paperId, Guid sectionId, CancellationToken ct)
    {
        var (exam, paper, section, students) = await ContextAsync(paperId, sectionId, ct);
        var sheet = await SheetRowAsync(paper, section, ct);
        if (sheet.Status != MarkSheetStatus.Draft) throw new DomainException("This mark sheet has already been submitted.", "already_submitted", isConflict: true);
        if (!await CanEnterAsync(exam, section, paper.SubjectId, sheet.Status, ct)) throw new ForbiddenException("You cannot submit this mark sheet.");
        var ids = students.Select(s => s.StudentId).ToList();
        var marks = await db.ExamMarks.Where(m => m.ExamPaperId == paperId && ids.Contains(m.StudentId)).ToListAsync(ct);
        var missing = students.Where(s => paper.Components.Any(c => !marks.Any(m => m.StudentId == s.StudentId && m.ComponentId == c.Id && (m.Marks != null || m.Code != MarkCode.None))))
            .Select(s => s.Name).ToList();
        if (missing.Count > 0)
            throw new DomainException("Marks are missing for " + string.Join(", ", missing.Take(5)) + (missing.Count > 5 ? $" and {missing.Count - 5} more" : "") +
                                      ". Use AB (absent) or EX (exempt) where there is no mark.", "marks_missing");

        var doubleEntry = await settings.GetBoolAsync(SettingKeys.ExamDoubleEntry, true, section.CampusId, ct);
        sheet.Status = doubleEntry ? MarkSheetStatus.Submitted : MarkSheetStatus.Verified;
        sheet.EnteredByUserId = currentUser.UserId;
        sheet.SubmittedAtUtc = clock.GetUtcNow().UtcDateTime;
        // Gradebook components are not typed twice.
        foreach (var m in marks.Where(m => paper.Components.First(c => c.Id == m.ComponentId).FromGradebook))
        {
            m.SecondMarks = m.Marks;
            m.SecondCode = m.Code;
        }
        if (!doubleEntry)
        {
            sheet.VerifiedByUserId = currentUser.UserId;
            sheet.VerifiedAtUtc = sheet.SubmittedAtUtc;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(paperId, sectionId, ct);
    }

    public async Task<MarkSheetView> ApproveAsync(Guid paperId, Guid sectionId, CancellationToken ct)
    {
        var (exam, paper, section, _) = await ContextAsync(paperId, sectionId, ct);
        currentUser.EnsurePermission(Permissions.Exams.Moderate, section.CampusId);
        var sheet = await SheetRowAsync(paper, section, ct);
        if (sheet.Status != MarkSheetStatus.Verified)
            throw new DomainException(sheet.Status == MarkSheetStatus.Submitted ? "The verification entry must match before approval." : "Only a verified sheet can be approved.",
                "not_verified", isConflict: true);
        if (exam.ResultStatus != ResultStatus.NotReady) throw new DomainException("Results are already finalised.", "results_finalised", isConflict: true);
        sheet.Status = MarkSheetStatus.Approved;
        sheet.ApprovedByUserId = currentUser.UserId;
        sheet.ApprovedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return await GetAsync(paperId, sectionId, ct);
    }

    /// <summary>Controlled re-open: back to draft with a reason (kept on the sheet and in the audit trail); the verification entry is cleared.</summary>
    public async Task<MarkSheetView> ReopenAsync(Guid paperId, Guid sectionId, string reason, CancellationToken ct)
    {
        var (exam, paper, section, students) = await ContextAsync(paperId, sectionId, ct);
        currentUser.EnsurePermission(Permissions.Exams.Moderate, section.CampusId);
        if (exam.ResultStatus != ResultStatus.NotReady)
            throw new DomainException("Results are finalised; withdraw them (Results tab) before re-opening marks.", "results_finalised", isConflict: true);
        var sheet = await SheetRowAsync(paper, section, ct);
        if (sheet.Status == MarkSheetStatus.Draft) throw new DomainException("The sheet is already open.", "already_open", isConflict: true);
        sheet.Status = MarkSheetStatus.Draft;
        sheet.ReopenCount++;
        sheet.ReopenReason = reason.Trim();
        sheet.VerifiedByUserId = sheet.ApprovedByUserId = null;
        sheet.VerifiedAtUtc = sheet.ApprovedAtUtc = null;
        var ids = students.Select(s => s.StudentId).ToList();
        var typed = paper.Components.Where(c => !c.FromGradebook).Select(c => c.Id).ToList();
        foreach (var m in await db.ExamMarks.Where(m => m.ExamPaperId == paperId && ids.Contains(m.StudentId) && typed.Contains(m.ComponentId)).ToListAsync(ct))
        {
            m.SecondMarks = null;
            m.SecondCode = null;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(paperId, sectionId, ct);
    }

    // ------------------------------------------------------------------ Excel

    public async Task<byte[]> TemplateAsync(Guid paperId, Guid sectionId, CancellationToken ct)
    {
        var view = await GetAsync(paperId, sectionId, ct);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Marks");
        var typed = view.Components.Where(c => !c.FromGradebook).ToList();
        string[] head = ["AdmissionNo", "Name", "Roll", .. typed.Select(c => $"{c.Name} (/{c.MaxMarks:0.##})")];
        for (var i = 0; i < head.Length; i++)
        {
            ws.Cell(1, i + 1).Value = head[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        var row = 2;
        foreach (var s in view.Students)
        {
            ws.Cell(row, 1).Value = s.AdmissionNo;
            ws.Cell(row, 2).Value = s.Name;
            if (s.RollNo is { } roll) ws.Cell(row, 3).Value = roll;
            for (var i = 0; i < typed.Count; i++)
            {
                var cell = s.Cells.First(c => c.ComponentId == typed[i].Id);
                if (cell.Code != MarkCode.None) ws.Cell(row, 4 + i).Value = Code(cell.Code);
                else if (cell.Marks is { } m) ws.Cell(row, 4 + i).Value = m;
            }
            row++;
        }
        ws.Columns().AdjustToContents();
        var help = wb.AddWorksheet("Help");
        help.Cell(1, 1).Value = $"{view.ExamName} · {view.ClassName} · {view.SectionLabel} · {view.SubjectName}";
        help.Cell(2, 1).Value = "Type marks in the component columns. Use AB for absent and EX for exempt. Do not change the AdmissionNo column.";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<MarkImportResult> ImportAsync(Guid paperId, Guid sectionId, Stream xlsx, bool dryRun, bool secondEntry, CancellationToken ct)
    {
        var view = await GetAsync(paperId, sectionId, ct);
        var typed = view.Components.Where(c => !c.FromGradebook).ToList();
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheet(1);
        var header = ws.Row(1).CellsUsed().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        if (!header.TryGetValue("AdmissionNo", out var admissionCol)) throw new DomainException("The file has no AdmissionNo column; download the template first.", "invalid_template");
        var columns = typed.ToDictionary(c => c.Id, c => header.FirstOrDefault(h => h.Key.StartsWith(c.Name, StringComparison.OrdinalIgnoreCase)).Value);
        if (columns.Values.Any(v => v == 0)) throw new DomainException("A component column is missing; download the template again.", "invalid_template");

        var students = view.Students.ToDictionary(s => s.AdmissionNo, StringComparer.OrdinalIgnoreCase);
        var entries = new List<MarkInput>();
        var problems = new List<MarkImportRow>();
        var rows = 0;
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            rows++;
            var adm = row.Cell(admissionCol).GetFormattedString().Trim();
            var errors = new List<string>();
            if (!students.TryGetValue(adm, out var s)) errors.Add($"'{adm}' is not in this class for this subject.");
            foreach (var c in typed)
            {
                var text = row.Cell(columns[c.Id]).GetFormattedString().Trim();
                MarkCode code = text.ToUpperInvariant() switch { "AB" => MarkCode.Absent, "EX" => MarkCode.Exempt, _ => MarkCode.None };
                decimal? marks = null;
                if (code == MarkCode.None && text.Length > 0)
                {
                    if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) && m >= 0 && m <= c.MaxMarks) marks = m;
                    else errors.Add($"{c.Name}: '{text}' is not a mark between 0 and {c.MaxMarks:0.##}, AB or EX.");
                }
                if (s is not null && (marks is not null || code != MarkCode.None)) entries.Add(new MarkInput(s.StudentId, c.Id, marks, code));
            }
            if (errors.Count > 0) problems.Add(new MarkImportRow(row.RowNumber(), adm, s?.Name, errors));
        }
        var valid = rows - problems.Count;
        if (dryRun || problems.Count > 0) return new MarkImportResult(dryRun, rows, valid, false, problems);
        await SaveAsync(paperId, sectionId, new SaveMarksRequest(entries, secondEntry), ct);
        return new MarkImportResult(false, rows, valid, true, problems);
    }

    // ------------------------------------------------------------------ calculation

    /// <summary>
    /// Subject % = Σ(weight × marks/max) / Σ(weight) over components that are not exempt; absent counts as 0. All
    /// components exempt → "EX" (left out of totals); every sat component absent → "AB". Null while marks are missing.
    /// </summary>
    public static (decimal? Percent, string? Code) SubjectPercent(IReadOnlyList<ComponentScore> components)
    {
        var counted = components.Where(c => c.Code != MarkCode.Exempt && c.WeightPercent > 0).ToList();
        if (components.Count > 0 && counted.Count == 0) return (null, "EX");
        if (counted.Count > 0 && counted.All(c => c.Code == MarkCode.Absent)) return (0, "AB");
        decimal weighted = 0, weights = 0;
        foreach (var c in counted)
        {
            if (c.Code == MarkCode.None && c.Marks is null) return (null, null);
            var got = c.Code == MarkCode.Absent ? 0 : c.Marks!.Value;
            weighted += c.MaxMarks == 0 ? 0 : got / c.MaxMarks * c.WeightPercent;
            weights += c.WeightPercent;
        }
        return weights == 0 ? (null, null) : (Math.Round(weighted * 100 / weights, 2), null);
    }

    public static string Code(MarkCode code) => code switch { MarkCode.Absent => "AB", MarkCode.Exempt => "EX", _ => "" };

    // ------------------------------------------------------------------ internals

    private async Task WriteAsync(ExamPaper paper, SectionInfo section, List<RosterStudent> students, MarkSheet sheet, IReadOnlyList<MarkInput> entries,
        bool second, bool moderatorEdit, CancellationToken ct)
    {
        var ids = students.Select(s => s.StudentId).ToHashSet();
        var components = paper.Components.ToDictionary(c => c.Id);
        var existing = await db.ExamMarks.Where(m => m.ExamPaperId == paper.Id && ids.Contains(m.StudentId)).ToListAsync(ct);
        foreach (var e in entries)
        {
            if (!ids.Contains(e.StudentId)) throw new DomainException("A student in the request does not sit this paper in this class.", "not_candidate");
            if (!components.TryGetValue(e.ComponentId, out var c)) throw new DomainException("A component in the request is not part of this paper.", "bad_component");
            // Gradebook components are filled from the gradebook but may be typed (e.g. AB/EX, or no CA on record); they are not entered twice.
            if (c.FromGradebook && second) continue;
            var marks = e.Code == MarkCode.None ? e.Marks : null;
            if (marks is { } m && (m < 0 || m > c.MaxMarks)) throw new DomainException($"{c.Name} marks must be between 0 and {c.MaxMarks:0.##}.", "marks_out_of_range");
            var row = existing.FirstOrDefault(x => x.StudentId == e.StudentId && x.ComponentId == c.Id);
            if (row is null)
            {
                if (second) throw new DomainException("The verification entry covers marks that were never entered.", "no_first_entry");
                row = new ExamMark { CampusId = section.CampusId, ExamPaperId = paper.Id, ComponentId = c.Id, StudentId = e.StudentId };
                db.ExamMarks.Add(row);
                existing.Add(row);
            }
            if (second)
            {
                row.SecondMarks = marks;
                row.SecondCode = e.Code;
            }
            else
            {
                row.Marks = marks;
                row.Code = e.Code;
                // A moderator's correction after submission stands for both entries; so does a gradebook component.
                if (moderatorEdit || c.FromGradebook)
                {
                    row.SecondMarks = marks;
                    row.SecondCode = e.Code;
                }
            }
        }

        if (second || (moderatorEdit && sheet.Status == MarkSheetStatus.Submitted))
        {
            var typed = paper.Components.Where(c => !c.FromGradebook).Select(c => c.Id).ToHashSet();
            var rows = existing.Where(m => typed.Contains(m.ComponentId)).ToList();
            var allMatch = rows.Count > 0 && rows.All(m => m.SecondCode is not null && m.SecondCode == m.Code && m.SecondMarks == m.Marks)
                           && students.All(s => typed.All(t => rows.Any(m => m.StudentId == s.StudentId && m.ComponentId == t)));
            if (allMatch)
            {
                sheet.Status = MarkSheetStatus.Verified;
                sheet.VerifiedByUserId = currentUser.UserId;
                sheet.VerifiedAtUtc = clock.GetUtcNow().UtcDateTime;
            }
        }
    }

    private async Task<bool> CanEnterAsync(Exam exam, SectionInfo section, Guid subjectId, MarkSheetStatus status, CancellationToken ct)
    {
        if (exam.ResultStatus != ResultStatus.NotReady || status == MarkSheetStatus.Approved) return false;
        if (currentUser.HasPermission(Permissions.Exams.Moderate, section.CampusId)) return true;
        return status == MarkSheetStatus.Draft && await access.CanAsync(section, subjectId, Permissions.Exams.Marks, Permissions.Exams.Moderate, ct, subjectOnly: true);
    }

    private async Task<(Exam, ExamPaper, SectionInfo, List<RosterStudent>)> ContextAsync(Guid paperId, Guid sectionId, CancellationToken ct)
    {
        var paper = await db.ExamPapers.AsNoTracking().Include(p => p.Components).FirstOrDefaultAsync(p => p.Id == paperId, ct) ?? throw new NotFoundException("Paper", paperId);
        var exam = await exams.LoadAsync(paper.ExamId, ct);
        var section = await lookups.SectionAsync(sectionId, ct);
        if (!currentUser.CanAccessCampus(section.CampusId)) throw new ForbiddenException("You do not have access to this campus.");
        if (section.YearGroupId != paper.YearGroupId) throw new DomainException("The section is not in this paper's class.", "wrong_section");
        var candidates = (await exams.CandidatesAsync(exam, ct)).Where(c => c.Section.Id == sectionId && c.PaperIds.Contains(paperId))
            .Select(c => new RosterStudent(c.StudentId, c.AdmissionNo, c.Name, c.RollNo)).ToList();
        return (exam, paper, section, candidates);
    }

    private async Task<MarkSheet> SheetRowAsync(ExamPaper paper, SectionInfo section, CancellationToken ct)
    {
        var sheet = await db.MarkSheets.FirstOrDefaultAsync(s => s.ExamPaperId == paper.Id && s.SectionId == section.Id, ct);
        if (sheet is not null) return sheet;
        sheet = new MarkSheet { CampusId = section.CampusId, ExamPaperId = paper.Id, SectionId = section.Id };
        db.MarkSheets.Add(sheet);
        return sheet;
    }

    private async Task<GradeScale?> SchemeAsync(Exam exam, Guid yearGroupId, CancellationToken ct)
    {
        var overrideId = await db.ExamClasses.AsNoTracking().Where(c => c.ExamId == exam.Id && c.YearGroupId == yearGroupId).Select(c => c.GradingSchemeId).FirstOrDefaultAsync(ct);
        return await grading.ForYearGroupAsync(yearGroupId, overrideId, ct);
    }
}
