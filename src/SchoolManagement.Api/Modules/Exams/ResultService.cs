using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Api.Modules.Attendance;
using SchoolManagement.Api.Modules.Learning;
using SchoolManagement.Api.Modules.Students.Services;
using SchoolManagement.Domain.Attendance;
using SchoolManagement.Domain.Exams;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Domain.Students;
using SchoolManagement.Infrastructure.Documents;
using SchoolManagement.Infrastructure.Jobs;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Exams;

public record ComponentResult(string Name, decimal? Marks, decimal MaxMarks, string? Code);

public record SubjectResult(Guid SubjectId, string Code, string Name, IReadOnlyList<ComponentResult> Components, decimal? Percent, string? Grade, string? Descriptor,
    string? DescriptorBn, decimal? GradePoint, bool Passed, string? Status, decimal? HighestInSection);

public record ResultRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, Guid SectionId, string SectionLabel, Guid CampusId, Guid YearGroupId, decimal Percent,
    string? Grade, decimal? Gpa, bool Passed, int FailedSubjects, int? SectionRank, int? ClassRank, decimal? Percentile, decimal? AttendancePercent,
    IReadOnlyList<SubjectResult> Subjects, bool Complete);

public record SubjectHead(Guid SubjectId, string Code, string Name);

public record TabulationView(Guid ExamId, string ExamName, ResultStatus Status, bool Preview, IReadOnlyList<string> PendingSheets, IReadOnlyList<SubjectHead> Subjects,
    IReadOnlyList<ResultRow> Students);

public record RemarkInput(Guid StudentId, string? TeacherRemark, string? PrincipalRemark, Conduct? Conduct, string? CoCurricular);

public record SaveRemarksRequest(List<RemarkInput> Entries);

public record RemarkRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, string? TeacherRemark, string? PrincipalRemark, Conduct? Conduct, string? CoCurricular);

public record RemarksView(Guid ExamId, Guid SectionId, string SectionLabel, bool CanEditTeacher, bool CanEditPrincipal, IReadOnlyList<RemarkRow> Students);

public record PublishRequest(DateTime? PublishAtUtc);

public record StudentResultSummary(Guid ExamId, string ExamName, ExamType Type, string AcademicYear, DateOnly EndDate, decimal Percent, string? Grade, decimal? Gpa,
    bool Passed, int? SectionRank, DateTime? PublishedAtUtc);

public record VerificationView(bool Found, bool Intact, string Message, string? School, string? StudentName, string? AdmissionNo, string? ClassName, string? ExamName,
    string? AcademicYear, decimal? Percent, string? Grade, decimal? Gpa, bool? Passed, DateTime? IssuedAtUtc);

public record TranscriptExam(Guid ExamId, string ExamName, ExamType Type, decimal Percent, string? Grade, decimal? Gpa, bool Passed, int? ClassRank);

public record TranscriptYear(Guid AcademicYearId, string AcademicYear, string ClassName, IReadOnlyList<TranscriptExam> Exams);

public record TranscriptMark(string AcademicYear, string ExamName, decimal? Percent, string? Grade, string? Status);

public record TranscriptSubject(string SubjectName, IReadOnlyList<TranscriptMark> Marks);

public record Transcript(Guid StudentId, string AdmissionNo, string Name, string? NameBn, DateOnly DateOfBirth, string Campus, DateOnly AdmissionDate,
    IReadOnlyList<TranscriptYear> Years, IReadOnlyList<TranscriptSubject> Subjects, decimal? CumulativeGpa, decimal? CumulativePercent);

public record GradeCount(string Grade, int Count);

public record SubjectAnalysis(Guid SubjectId, string Name, int Candidates, decimal Average, decimal PassPercent, decimal Highest, decimal Lowest,
    IReadOnlyList<GradeCount> Grades, decimal? CompareAverage);

public record GroupAnalysis(string Name, int Candidates, decimal Average, decimal PassPercent);

public record HeatCell(string Section, string Subject, decimal Average);

public record TopStudent(string Name, string AdmissionNo, string ClassLabel, decimal Percent, string? Grade, decimal? Gpa);

public record AtRiskStudent(string Name, string AdmissionNo, string ClassLabel, decimal Percent, IReadOnlyList<string> FailedSubjects);

public record ResultAnalysis(Guid ExamId, string ExamName, bool Preview, int Candidates, int Passed, decimal PassPercent, decimal Average, IReadOnlyList<GradeCount> OverallGrades,
    IReadOnlyList<SubjectAnalysis> Subjects, IReadOnlyList<GroupAnalysis> Sections, IReadOnlyList<GroupAnalysis> Teachers, IReadOnlyList<GroupAnalysis> Campuses,
    IReadOnlyList<HeatCell> Heatmap, IReadOnlyList<TopStudent> TopPerformers, IReadOnlyList<AtRiskStudent> AtRisk, string? CompareExamName, decimal? CompareAverage,
    decimal? ComparePassPercent);

/// <summary>
/// EXM-04/05/06/10: grade calculation from approved mark sheets, finalised report cards (signed, QR-verifiable),
/// embargoed publication with guardian notification, transcripts and result analysis.
/// </summary>
public sealed class ResultService(
    AppDbContext db,
    AcademicLookups lookups,
    ExamService exams,
    Grading grading,
    GuardianNotifier notifier,
    PortalAccess portal,
    ISettingsService settings,
    IQrCodeService qr,
    IConfiguration configuration,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ------------------------------------------------------------------ tabulation & finalising

    /// <summary>The tabulation sheet: frozen results once finalised, otherwise a live preview from the marks entered so far.</summary>
    public async Task<TabulationView> TabulationAsync(Guid examId, Guid? sectionId, CancellationToken ct)
    {
        var exam = await exams.LoadAsync(examId, ct);
        List<ResultRow> rows;
        List<string> pending = [];
        if (exam.ResultStatus == ResultStatus.NotReady)
            (rows, pending) = await ComputeAsync(exam, ct);
        else
            rows = await StoredRowsAsync(exam, ct);
        rows = rows.Where(r => currentUser.CanAccessCampus(r.CampusId) && (sectionId == null || r.SectionId == sectionId)).ToList();
        var heads = rows.SelectMany(r => r.Subjects).DistinctBy(s => s.SubjectId).OrderBy(s => s.Name).Select(s => new SubjectHead(s.SubjectId, s.Code, s.Name)).ToList();
        return new TabulationView(exam.Id, exam.Name, exam.ResultStatus, exam.ResultStatus == ResultStatus.NotReady, pending, heads,
            rows.OrderBy(r => r.SectionLabel).ThenBy(r => r.RollNo ?? int.MaxValue).ThenBy(r => r.Name).ToList());
    }

    /// <summary>Freezes the results into report cards. Every mark sheet must be approved.</summary>
    public async Task<TabulationView> FinaliseAsync(Guid examId, CancellationToken ct)
    {
        var exam = await exams.LoadAsync(examId, ct, tracked: true);
        EnsureManage(exam);
        if (exam.ResultStatus is ResultStatus.Published or ResultStatus.Scheduled)
            throw new DomainException("Results are published or scheduled; withdraw them first.", "results_published", isConflict: true);
        var (rows, pending) = await ComputeAsync(exam, ct);
        if (pending.Count > 0)
            throw new DomainException($"{pending.Count} mark sheet(s) are not approved yet: " + string.Join(", ", pending.Take(5)) + (pending.Count > 5 ? " …" : "") + ".",
                "sheets_pending", isConflict: true);
        if (rows.Count == 0) throw new DomainException("No students sit this exam.", "no_candidates");

        var existing = await db.ExamResults.IgnoreQueryFilters().Where(r => r.ExamId == examId).ToDictionaryAsync(r => r.StudentId, ct);
        var remarks = await db.ReportCardRemarks.IgnoreQueryFilters().AsNoTracking().Where(r => r.ExamId == examId).ToDictionaryAsync(r => r.StudentId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        using (db.ElevateToSystem())
        {
            foreach (var gone in existing.Values.Where(e => rows.All(r => r.StudentId != e.StudentId))) db.ExamResults.Remove(gone);
            foreach (var r in rows)
            {
                if (!existing.TryGetValue(r.StudentId, out var row))
                {
                    row = new ExamResult
                    {
                        CampusId = r.CampusId, AcademicYearId = exam.AcademicYearId, ExamId = exam.Id, StudentId = r.StudentId, SubjectsJson = "[]",
                        VerificationCode = await NewCodeAsync(ct), Signature = ""
                    };
                    db.ExamResults.Add(row);
                }
                row.CampusId = r.CampusId;
                row.SectionId = r.SectionId;
                row.YearGroupId = r.YearGroupId;
                row.Percent = r.Percent;
                row.Grade = r.Grade;
                row.Gpa = r.Gpa;
                row.Passed = r.Passed;
                row.FailedSubjects = r.FailedSubjects;
                row.SectionRank = r.SectionRank;
                row.ClassRank = r.ClassRank;
                row.Percentile = r.Percentile;
                row.AttendancePercent = r.AttendancePercent;
                row.SubjectsJson = JsonSerializer.Serialize(r.Subjects, Json);
                var remark = remarks.GetValueOrDefault(r.StudentId);
                row.TeacherRemark = remark?.TeacherRemark;
                row.PrincipalRemark = remark?.PrincipalRemark;
                row.Conduct = remark?.Conduct;
                row.CoCurricular = remark?.CoCurricular;
                row.GeneratedAtUtc = now;
                row.Signature = Sign(row);
            }
            exam.ResultStatus = ResultStatus.Finalised;
            exam.FinalisedAtUtc = now;
            await db.SaveChangesAsync(ct);
        }
        return await TabulationAsync(examId, null, ct);
    }

    /// <summary>Publishes now, or at <see cref="PublishRequest.PublishAtUtc"/> (embargo).</summary>
    public async Task<ExamView> PublishAsync(Guid examId, PublishRequest r, CancellationToken ct)
    {
        var exam = await exams.LoadAsync(examId, ct, tracked: true);
        EnsureManage(exam);
        if (exam.ResultStatus == ResultStatus.NotReady) throw new DomainException("Finalise the results before publishing.", "not_finalised", isConflict: true);
        if (exam.ResultStatus == ResultStatus.Published) throw new DomainException("The results are already published.", "already_published", isConflict: true);
        var now = clock.GetUtcNow().UtcDateTime;
        if (r.PublishAtUtc is { } at && at > now)
        {
            exam.ResultStatus = ResultStatus.Scheduled;
            exam.PublishAtUtc = at;
            await db.SaveChangesAsync(ct);
        }
        else await PublishNowAsync(exam, ct);
        return await exams.GetAsync(examId, ct);
    }

    /// <summary>Makes the report cards visible to families and tells the guardians (Appendix C: result published).</summary>
    public async Task PublishNowAsync(Exam exam, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        exam.ResultStatus = ResultStatus.Published;
        exam.PublishedAtUtc = now;
        exam.PublishAtUtc ??= now;
        var results = await db.ExamResults.IgnoreQueryFilters().AsNoTracking().Where(x => x.ExamId == exam.Id).ToListAsync(ct);
        var sections = await lookups.SectionsAsync(results.Select(x => x.SectionId), ct);
        var names = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => results.Select(x => x.StudentId).Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FirstName + (s.LastName == null ? "" : " " + s.LastName), ct);
        foreach (var x in results)
            await notifier.NotifyAsync(x.StudentId, x.CampusId, TemplateCodes.ResultPublished, new Dictionary<string, string?>
            {
                ["ChildName"] = names.GetValueOrDefault(x.StudentId), ["ClassName"] = sections.GetValueOrDefault(x.SectionId)?.Label, ["Exam"] = exam.Name,
                ["Summary"] = Summary(x)
            }, nameof(ExamResult), x.Id.ToString(), ct, includeStudent: true);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Takes published or scheduled results back to "finalised" (e.g. to correct marks).</summary>
    public async Task<ExamView> WithdrawAsync(Guid examId, CancellationToken ct)
    {
        var exam = await exams.LoadAsync(examId, ct, tracked: true);
        EnsureManage(exam);
        if (exam.ResultStatus is not (ResultStatus.Published or ResultStatus.Scheduled)) throw new DomainException("The results are not published.", "not_published", isConflict: true);
        exam.ResultStatus = ResultStatus.Finalised;
        exam.PublishAtUtc = null;
        exam.PublishedAtUtc = null;
        await db.SaveChangesAsync(ct);
        return await exams.GetAsync(examId, ct);
    }

    /// <summary>Unfreezes finalised (unpublished) results so mark sheets can be re-opened.</summary>
    public async Task<ExamView> UnfinaliseAsync(Guid examId, CancellationToken ct)
    {
        var exam = await exams.LoadAsync(examId, ct, tracked: true);
        EnsureManage(exam);
        if (exam.ResultStatus != ResultStatus.Finalised) throw new DomainException("Only finalised, unpublished results can be re-opened.", "not_finalised", isConflict: true);
        exam.ResultStatus = ResultStatus.NotReady;
        await db.SaveChangesAsync(ct);
        return await exams.GetAsync(examId, ct);
    }

    // ------------------------------------------------------------------ remarks (EXM-05)

    public async Task<RemarksView> RemarksAsync(Guid examId, Guid sectionId, CancellationToken ct)
    {
        var exam = await exams.LoadAsync(examId, ct);
        var section = await lookups.SectionAsync(sectionId, ct);
        if (!currentUser.CanAccessCampus(section.CampusId)) throw new ForbiddenException("You do not have access to this campus.");
        var students = (await exams.CandidatesAsync(exam, ct)).Where(c => c.Section.Id == sectionId).OrderBy(c => c.RollNo ?? int.MaxValue).ThenBy(c => c.Name).ToList();
        var ids = students.Select(s => s.StudentId).ToList();
        var remarks = await db.ReportCardRemarks.AsNoTracking().Where(r => r.ExamId == examId && ids.Contains(r.StudentId)).ToDictionaryAsync(r => r.StudentId, ct);
        var open = exam.ResultStatus is not (ResultStatus.Published or ResultStatus.Scheduled);
        return new RemarksView(examId, sectionId, section.Label, open && CanTeacherRemark(section), open && currentUser.HasPermission(Permissions.Exams.Manage, section.CampusId),
            students.Select(s =>
            {
                var r = remarks.GetValueOrDefault(s.StudentId);
                return new RemarkRow(s.StudentId, s.AdmissionNo, s.Name, s.RollNo, r?.TeacherRemark, r?.PrincipalRemark, r?.Conduct, r?.CoCurricular);
            }).ToList());
    }

    /// <summary>Class teacher: remark, conduct, co-curricular. Exam office/principal: also the principal's remark. Finalised cards are updated too.</summary>
    public async Task<RemarksView> SaveRemarksAsync(Guid examId, Guid sectionId, SaveRemarksRequest r, CancellationToken ct)
    {
        var view = await RemarksAsync(examId, sectionId, ct);
        if (!view.CanEditTeacher && !view.CanEditPrincipal) throw new ForbiddenException("Only the class teacher or the exam office can write these remarks.");
        var section = await lookups.SectionAsync(sectionId, ct);
        var ids = view.Students.Select(s => s.StudentId).ToHashSet();
        var existing = await db.ReportCardRemarks.Where(x => x.ExamId == examId && ids.Contains(x.StudentId)).ToDictionaryAsync(x => x.StudentId, ct);
        var results = await db.ExamResults.Where(x => x.ExamId == examId && ids.Contains(x.StudentId)).ToDictionaryAsync(x => x.StudentId, ct);
        foreach (var e in r.Entries)
        {
            if (!ids.Contains(e.StudentId)) throw new DomainException("A student in the request is not in this section.", "not_in_section");
            if (!existing.TryGetValue(e.StudentId, out var row))
            {
                row = new ReportCardRemark { CampusId = section.CampusId, ExamId = examId, StudentId = e.StudentId };
                db.ReportCardRemarks.Add(row);
            }
            if (view.CanEditTeacher)
            {
                row.TeacherRemark = LearningText.Clean(e.TeacherRemark);
                row.Conduct = e.Conduct;
                row.CoCurricular = LearningText.Clean(e.CoCurricular);
            }
            if (view.CanEditPrincipal) row.PrincipalRemark = LearningText.Clean(e.PrincipalRemark);
            if (results.TryGetValue(e.StudentId, out var res))
            {
                res.TeacherRemark = row.TeacherRemark;
                res.PrincipalRemark = row.PrincipalRemark;
                res.Conduct = row.Conduct;
                res.CoCurricular = row.CoCurricular;
            }
        }
        await db.SaveChangesAsync(ct);
        return await RemarksAsync(examId, sectionId, ct);
    }

    // ------------------------------------------------------------------ report cards

    /// <summary>Printable bilingual report cards (one page each) for a section, a campus or one student. Finalised results only.</summary>
    public async Task<string> ReportCardsAsync(Guid examId, Guid? sectionId, Guid? studentId, string baseUrl, CancellationToken ct)
    {
        var exam = await db.Exams.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && !e.IsDeleted, ct) ?? throw new NotFoundException("Exam", examId);
        if (exam.ResultStatus == ResultStatus.NotReady) throw new DomainException("Report cards are available once the results are finalised.", "not_finalised", isConflict: true);
        var q = db.ExamResults.IgnoreQueryFilters().AsNoTracking().Where(r => r.ExamId == examId);
        if (sectionId is not null) q = q.Where(r => r.SectionId == sectionId);
        if (studentId is not null) q = q.Where(r => r.StudentId == studentId);
        var results = await q.ToListAsync(ct);
        if (studentId is null) results = results.Where(r => currentUser.CanAccessCampus(r.CampusId)).ToList();
        if (results.Count == 0) throw new DomainException("No report cards match.", "no_results");

        var sections = await lookups.SectionsAsync(results.Select(r => r.SectionId), ct);
        var ids = results.Select(r => r.StudentId).ToList();
        var students = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var rolls = await db.StudentEnrolments.IgnoreQueryFilters().AsNoTracking().Where(e => ids.Contains(e.StudentId) && e.AcademicYearId == exam.AcademicYearId && !e.IsDeleted)
            .GroupBy(e => e.StudentId).Select(g => new { g.Key, Roll = g.OrderByDescending(e => e.StartDate).Select(e => e.RollNo).FirstOrDefault() })
            .ToDictionaryAsync(x => x.Key, x => x.Roll, ct);
        var org = await db.Organisations.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(ct);
        var campuses = await db.Campuses.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var year = await db.AcademicYears.IgnoreQueryFilters().AsNoTracking().Where(y => y.Id == exam.AcademicYearId).Select(y => y.Name).FirstAsync(ct);
        var scales = new Dictionary<Guid, GradeScale?>();
        var portalUrl = (await settings.GetAsync(SettingKeys.SchoolPortalUrl, null, ct))?.TrimEnd('/');
        var verifyBase = string.IsNullOrWhiteSpace(portalUrl) ? baseUrl.TrimEnd('/') : portalUrl;

        var sb = new StringBuilder();
        sb.Append($"<!doctype html><html><head><meta charset=\"utf-8\"><title>Report cards — {ExamService.E(exam.Name)}</title><style>{ReportCss}</style></head><body>");
        foreach (var r in results.OrderBy(r => sections.GetValueOrDefault(r.SectionId)?.Label).ThenBy(r => rolls.GetValueOrDefault(r.StudentId) ?? int.MaxValue))
        {
            if (!scales.TryGetValue(r.YearGroupId, out var scale))
                scales[r.YearGroupId] = scale = await grading.ForYearGroupAsync(r.YearGroupId,
                    await db.ExamClasses.AsNoTracking().Where(c => c.ExamId == examId && c.YearGroupId == r.YearGroupId).Select(c => c.GradingSchemeId).FirstOrDefaultAsync(ct), ct);
            var showRank = await settings.GetBoolAsync(SettingKeys.ExamShowRank, true, r.CampusId, ct);
            var principal = await settings.GetAsync(SettingKeys.PrincipalName, r.CampusId, ct);
            sb.Append(RenderCard(exam, year, org?.Name ?? "", org?.NameBn, campuses.GetValueOrDefault(r.CampusId)?.Name ?? "", students[r.StudentId],
                sections.GetValueOrDefault(r.SectionId)?.Label ?? "", rolls.GetValueOrDefault(r.StudentId), r, scale, showRank, principal, $"{verifyBase}/public/verify/{r.VerificationCode}"));
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ portal & verification

    public async Task<List<StudentResultSummary>> ForStudentAsync(Guid studentId, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        var rows = await db.ExamResults.IgnoreQueryFilters().AsNoTracking().Where(r => r.StudentId == studentId)
            .Join(db.Exams.IgnoreQueryFilters().Where(e => !e.IsDeleted && e.ResultStatus == ResultStatus.Published), r => r.ExamId, e => e.Id, (r, e) => new { r, e })
            .ToListAsync(ct);
        var years = await db.AcademicYears.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(y => y.Id, y => y.Name, ct);
        return rows.OrderByDescending(x => x.e.EndDate).Select(x => new StudentResultSummary(x.e.Id, x.e.Name, x.e.Type, years.GetValueOrDefault(x.e.AcademicYearId, "?"),
            x.e.EndDate, x.r.Percent, x.r.Grade, x.r.Gpa, x.r.Passed, x.r.SectionRank, x.e.PublishedAtUtc)).ToList();
    }

    public async Task<string> ReportCardForStudentAsync(Guid examId, Guid studentId, string baseUrl, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        if (!await db.Exams.IgnoreQueryFilters().AnyAsync(e => e.Id == examId && !e.IsDeleted && e.ResultStatus == ResultStatus.Published, ct))
            throw new ForbiddenException("These results have not been published yet.");
        return await ReportCardsAsync(examId, null, studentId, baseUrl, ct);
    }

    /// <summary>Public QR check: is this report card genuine and unaltered?</summary>
    public async Task<VerificationView> VerifyAsync(string code, CancellationToken ct)
    {
        var c = code.Trim().ToUpperInvariant();
        using var _ = db.ElevateToSystem();
        var r = await db.ExamResults.AsNoTracking().FirstOrDefaultAsync(x => x.VerificationCode == c, ct);
        if (r is null) return new VerificationView(false, false, "No report card has this verification code.", null, null, null, null, null, null, null, null, null, null, null);
        var exam = await db.Exams.IgnoreQueryFilters().AsNoTracking().FirstAsync(e => e.Id == r.ExamId, ct);
        if (exam.ResultStatus != ResultStatus.Published)
            return new VerificationView(false, false, "This result has not been published.", null, null, null, null, null, null, null, null, null, null, null);
        var student = await db.Students.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == r.StudentId, ct);
        var section = (await lookups.SectionsAsync([r.SectionId], ct)).GetValueOrDefault(r.SectionId)?.Label;
        var school = await db.Organisations.IgnoreQueryFilters().AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct);
        var year = await db.AcademicYears.IgnoreQueryFilters().AsNoTracking().Where(y => y.Id == exam.AcademicYearId).Select(y => y.Name).FirstAsync(ct);
        var intact = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(r)), Encoding.ASCII.GetBytes(r.Signature));
        return new VerificationView(true, intact, intact ? "Genuine report card issued by the school." : "The stored result does not match its signature. Contact the school.",
            school, student.FullName, student.AdmissionNo, section, exam.Name, year, r.Percent, r.Grade, r.Gpa, r.Passed, r.GeneratedAtUtc);
    }

    // ------------------------------------------------------------------ transcripts (EXM-06)

    /// <summary>Multi-year transcript from published results. Staff may include finalised but unpublished exams.</summary>
    public async Task<Transcript> TranscriptAsync(Guid studentId, bool forPortal, CancellationToken ct)
    {
        if (forPortal) await portal.EnsureAsync(studentId, ct);
        var student = await (forPortal ? db.Students.IgnoreQueryFilters() : db.Students).AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct)
                      ?? throw new NotFoundException("Student", studentId);
        var rows = await db.ExamResults.IgnoreQueryFilters().AsNoTracking().Where(r => r.StudentId == studentId)
            .Join(db.Exams.IgnoreQueryFilters().Where(e => !e.IsDeleted && (e.ResultStatus == ResultStatus.Published || (!forPortal && e.ResultStatus != ResultStatus.NotReady))),
                r => r.ExamId, e => e.Id, (r, e) => new { r, e }).ToListAsync(ct);
        var years = await db.AcademicYears.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(y => y.Id, ct);
        var classes = await exams.ClassNamesAsync(ct);
        var campus = await db.Campuses.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == student.CampusId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "";

        var ordered = rows.OrderBy(x => years.GetValueOrDefault(x.e.AcademicYearId)?.StartDate).ThenBy(x => x.e.EndDate).ToList();
        var yearRows = ordered.GroupBy(x => x.e.AcademicYearId).Select(g => new TranscriptYear(g.Key, years.GetValueOrDefault(g.Key)?.Name ?? "?",
            classes.GetValueOrDefault(g.First().r.YearGroupId, "?"),
            g.Select(x => new TranscriptExam(x.e.Id, x.e.Name, x.e.Type, x.r.Percent, x.r.Grade, x.r.Gpa, x.r.Passed, x.r.ClassRank)).ToList())).ToList();
        var subjects = ordered.SelectMany(x => (JsonSerializer.Deserialize<List<SubjectResult>>(x.r.SubjectsJson, Json) ?? [])
                .Select(s => (s.Name, Mark: new TranscriptMark(years.GetValueOrDefault(x.e.AcademicYearId)?.Name ?? "?", x.e.Name, s.Percent, s.Grade, s.Status))))
            .GroupBy(x => x.Name).OrderBy(g => g.Key).Select(g => new TranscriptSubject(g.Key, g.Select(x => x.Mark).ToList())).ToList();

        // Cumulative figures use the year's final (or last) exam.
        var finals = ordered.GroupBy(x => x.e.AcademicYearId).Select(g => g.Where(x => x.e.Type == ExamType.Final).LastOrDefault() ?? g.Last()).ToList();
        var gpas = finals.Where(x => x.r.Gpa is not null).Select(x => x.r.Gpa!.Value).ToList();
        return new Transcript(student.Id, student.AdmissionNo, student.FullName, student.NameBn, student.DateOfBirth, campus, student.AdmissionDate, yearRows, subjects,
            gpas.Count == 0 ? null : Math.Round(gpas.Average(), 2), finals.Count == 0 ? null : Math.Round(finals.Average(x => x.r.Percent), 2));
    }

    public async Task<string> TranscriptHtmlAsync(Guid studentId, bool forPortal, CancellationToken ct)
    {
        var t = await TranscriptAsync(studentId, forPortal, ct);
        var org = await db.Organisations.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(ct);
        string E(string? s) => ExamService.E(s);
        var sb = new StringBuilder();
        sb.Append($"<!doctype html><html><head><meta charset=\"utf-8\"><title>Transcript — {E(t.Name)}</title><style>{ReportCss}</style></head><body><div class=\"page\">");
        sb.Append($"<div class=\"head\"><div><div class=\"school\">{E(org?.Name)}</div><div class=\"bn\">{E(org?.NameBn)}</div><div>{E(t.Campus)}</div></div>" +
                  "<div class=\"doc\">ACADEMIC TRANSCRIPT<br><span class=\"bn\">একাডেমিক ট্রান্সক্রিপ্ট</span></div></div>");
        sb.Append($"<table class=\"info\"><tr><td>Name / নাম</td><td><b>{E(t.Name)}</b> {E(t.NameBn)}</td><td>Admission no / ভর্তি নং</td><td>{E(t.AdmissionNo)}</td></tr>" +
                  $"<tr><td>Date of birth</td><td>{t.DateOfBirth:dd MMM yyyy}</td><td>Admitted</td><td>{t.AdmissionDate:dd MMM yyyy}</td></tr></table>");
        foreach (var y in t.Years)
        {
            sb.Append($"<h3>{E(y.AcademicYear)} · {E(y.ClassName)}</h3><table class=\"marks\"><tr><th>Exam</th><th>%</th><th>Grade</th><th>GPA</th><th>Result</th><th>Class rank</th></tr>");
            foreach (var x in y.Exams)
                sb.Append($"<tr><td>{E(x.ExamName)}</td><td>{x.Percent:0.##}</td><td>{E(x.Grade)}</td><td>{x.Gpa:0.00}</td><td>{(x.Passed ? "Passed" : "Not passed")}</td><td>{x.ClassRank}</td></tr>");
            sb.Append("</table>");
        }
        sb.Append("<h3>Subject history / বিষয়ভিত্তিক ফলাফল</h3><table class=\"marks\"><tr><th>Subject</th><th>Year · exam</th><th>%</th><th>Grade</th></tr>");
        foreach (var s in t.Subjects)
            foreach (var m in s.Marks)
                sb.Append($"<tr><td>{E(s.SubjectName)}</td><td>{E(m.AcademicYear)} · {E(m.ExamName)}</td><td>{(m.Status ?? m.Percent?.ToString("0.##"))}</td><td>{E(m.Grade)}</td></tr>");
        sb.Append("</table>");
        sb.Append($"<p class=\"summary\">Cumulative GPA: <b>{(t.CumulativeGpa is { } g ? g.ToString("0.00") : "—")}</b> · Cumulative %: <b>{(t.CumulativePercent is { } p ? p.ToString("0.##") : "—")}</b></p>");
        sb.Append($"<div class=\"signs\"><div>Issued {lookups.TodayDhaka:dd MMM yyyy}</div><div>______________________<br>Principal</div></div></div></body></html>");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ analysis (EXM-10)

    public async Task<ResultAnalysis> AnalysisAsync(Guid examId, Guid? compareExamId, CancellationToken ct)
    {
        var tab = await TabulationAsync(examId, null, ct);
        var rows = tab.Students.Where(r => r.Complete).ToList();
        var subjectLines = rows.SelectMany(r => r.Subjects.Where(s => s.Percent is not null && s.Status != "EX").Select(s => (Row: r, Subject: s))).ToList();

        Dictionary<Guid, decimal> compareSubjects = [];
        string? compareName = null;
        decimal? compareAverage = null, comparePass = null;
        if (compareExamId is { } cid)
        {
            var other = await TabulationAsync(cid, null, ct);
            var complete = other.Students.Where(r => r.Complete).ToList();
            compareName = other.ExamName;
            if (complete.Count > 0)
            {
                compareAverage = Math.Round(complete.Average(r => r.Percent), 2);
                comparePass = Pct(complete.Count(r => r.Passed), complete.Count);
                compareSubjects = complete.SelectMany(r => r.Subjects.Where(s => s.Percent is not null && s.Status != "EX"))
                    .GroupBy(s => s.SubjectId).ToDictionary(g => g.Key, g => Math.Round(g.Average(s => s.Percent!.Value), 2));
            }
        }

        var subjects = subjectLines.GroupBy(x => x.Subject.SubjectId).Select(g =>
        {
            var p = g.Select(x => x.Subject.Percent!.Value).ToList();
            return new SubjectAnalysis(g.Key, g.First().Subject.Name, g.Count(), Math.Round(p.Average(), 2), Pct(g.Count(x => x.Subject.Passed), g.Count()), p.Max(), p.Min(),
                g.GroupBy(x => x.Subject.Grade ?? "—").Select(x => new GradeCount(x.Key, x.Count())).OrderBy(x => x.Grade).ToList(),
                compareSubjects.TryGetValue(g.Key, out var c) ? c : null);
        }).OrderBy(s => s.Name).ToList();

        GroupAnalysis Group(string name, IReadOnlyCollection<ResultRow> list) =>
            new(name, list.Count, list.Count == 0 ? 0 : Math.Round(list.Average(r => r.Percent), 2), Pct(list.Count(r => r.Passed), list.Count));

        // Teacher comparison: each teacher's subject lines in the sections they are allocated to.
        var allocations = await db.TeacherAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(a => rows.Select(r => r.SectionId).Distinct().Contains(a.SectionId)).Select(a => new { a.SectionId, a.SubjectId, a.TeacherUserId }).ToListAsync(ct);
        var teacherNames = await lookups.UserNamesAsync(allocations.Select(a => (Guid?)a.TeacherUserId), ct);
        var teachers = allocations.GroupBy(a => a.TeacherUserId).Select(g =>
        {
            var lines = subjectLines.Where(x => g.Any(a => a.SectionId == x.Row.SectionId && a.SubjectId == x.Subject.SubjectId)).ToList();
            return new GroupAnalysis(teacherNames.GetValueOrDefault(g.Key, "?"), lines.Count, lines.Count == 0 ? 0 : Math.Round(lines.Average(x => x.Subject.Percent!.Value), 2),
                Pct(lines.Count(x => x.Subject.Passed), lines.Count));
        }).Where(t => t.Candidates > 0).OrderByDescending(t => t.Average).ToList();

        var campusNames = await db.Campuses.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var subjectNames = subjects.ToDictionary(s => s.SubjectId, s => s.Name);
        return new ResultAnalysis(examId, tab.ExamName, tab.Preview, rows.Count, rows.Count(r => r.Passed), Pct(rows.Count(r => r.Passed), rows.Count),
            rows.Count == 0 ? 0 : Math.Round(rows.Average(r => r.Percent), 2),
            rows.GroupBy(r => r.Grade ?? "—").Select(g => new GradeCount(g.Key, g.Count())).OrderBy(g => g.Grade).ToList(),
            subjects,
            rows.GroupBy(r => r.SectionLabel).OrderBy(g => g.Key).Select(g => Group(g.Key, g.ToList())).ToList(),
            teachers,
            rows.GroupBy(r => r.CampusId).Select(g => Group(campusNames.GetValueOrDefault(g.Key, "?"), g.ToList())).OrderBy(g => g.Name).ToList(),
            subjectLines.GroupBy(x => (x.Row.SectionLabel, x.Subject.SubjectId))
                .Select(g => new HeatCell(g.Key.SectionLabel, subjectNames.GetValueOrDefault(g.Key.SubjectId, "?"), Math.Round(g.Average(x => x.Subject.Percent!.Value), 1)))
                .OrderBy(h => h.Section).ThenBy(h => h.Subject).ToList(),
            rows.OrderByDescending(r => r.Percent).Take(10).Select(r => new TopStudent(r.Name, r.AdmissionNo, r.SectionLabel, r.Percent, r.Grade, r.Gpa)).ToList(),
            rows.Where(r => !r.Passed).OrderBy(r => r.Percent).Select(r => new AtRiskStudent(r.Name, r.AdmissionNo, r.SectionLabel, r.Percent,
                r.Subjects.Where(s => !s.Passed && s.Status != "EX").Select(s => s.Name).ToList())).ToList(),
            compareName, compareAverage, comparePass);
    }

    // ------------------------------------------------------------------ calculation

    /// <summary>Results from the marks entered so far, and the mark sheets that are not approved yet.</summary>
    public async Task<(List<ResultRow> Rows, List<string> Pending)> ComputeAsync(Exam exam, CancellationToken ct)
    {
        var candidates = await exams.CandidatesAsync(exam, ct);
        var papers = await db.ExamPapers.AsNoTracking().Include(p => p.Components).Where(p => p.ExamId == exam.Id).ToDictionaryAsync(p => p.Id, ct);
        var paperIds = papers.Keys.ToList();
        var marks = await db.ExamMarks.IgnoreQueryFilters().AsNoTracking().Where(m => paperIds.Contains(m.ExamPaperId)).ToListAsync(ct);
        var markLookup = marks.ToDictionary(m => (m.ComponentId, m.StudentId));
        var sheets = await db.MarkSheets.IgnoreQueryFilters().AsNoTracking().Where(s => paperIds.Contains(s.ExamPaperId)).ToDictionaryAsync(s => (s.ExamPaperId, s.SectionId), ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var overrides = await db.ExamClasses.AsNoTracking().Where(c => c.ExamId == exam.Id).ToDictionaryAsync(c => c.YearGroupId, c => c.GradingSchemeId, ct);
        var scales = new Dictionary<Guid, GradeScale?>();
        foreach (var yg in candidates.Select(c => c.Section.YearGroupId).Distinct())
            scales[yg] = await grading.ForYearGroupAsync(yg, overrides.GetValueOrDefault(yg), ct);
        var attendance = await AttendancePercentsAsync(exam, candidates.Select(c => c.StudentId).Distinct().ToList(), ct);

        var pending = candidates.SelectMany(c => c.PaperIds.Select(p => (Paper: p, c.Section))).DistinctBy(x => (x.Paper, x.Section.Id))
            .Where(x => sheets.GetValueOrDefault((x.Paper, x.Section.Id))?.Status != MarkSheetStatus.Approved)
            .Select(x => $"{x.Section.Label} {subjects.GetValueOrDefault(papers[x.Paper].SubjectId).Name}").OrderBy(s => s).ToList();

        var rows = new List<ResultRow>();
        foreach (var c in candidates)
        {
            var scale = scales.GetValueOrDefault(c.Section.YearGroupId);
            var lines = new List<SubjectResult>();
            foreach (var paper in c.PaperIds.Select(id => papers[id]).OrderBy(p => subjects.GetValueOrDefault(p.SubjectId).Name))
            {
                var comps = paper.Components.OrderBy(x => x.Sequence).ToList();
                var cells = comps.Select(x => (x, m: markLookup.GetValueOrDefault((x.Id, c.StudentId)))).ToList();
                var (pct, code) = MarkService.SubjectPercent(cells.Select(x => new ComponentScore(x.x.MaxMarks, x.x.WeightPercent, x.m?.Marks, x.m?.Code ?? MarkCode.None)).ToList());
                var band = pct is { } p && code != "EX" ? scale?.BandFor(p) : null;
                lines.Add(new SubjectResult(paper.SubjectId, subjects.GetValueOrDefault(paper.SubjectId).Code ?? "", subjects.GetValueOrDefault(paper.SubjectId).Name ?? "?",
                    cells.Select(x => new ComponentResult(x.x.Name, x.m?.Marks, x.x.MaxMarks, x.m is null ? null : MarkService.Code(x.m.Code) is { Length: > 0 } s ? s : null)).ToList(),
                    pct, band?.Grade, band?.Descriptor, band?.DescriptorBn, band?.GradePoint, code == "EX" || (code != "AB" && (band?.IsPass ?? pct >= 40)), code, null));
            }
            var counted = lines.Where(l => l.Status != "EX").ToList();
            var complete = counted.All(l => l.Percent is not null);
            var percent = counted.Count == 0 || !complete ? 0 : Math.Round(counted.Average(l => l.Percent!.Value), 2);
            var zeroOnFail = await settings.GetBoolAsync(SettingKeys.ExamGpaZeroOnFail, true, c.Section.CampusId, ct);
            var gpa = scale?.UsesGpa == true ? Grading.Gpa(counted.Select(l => (l.GradePoint, l.Passed)).ToList(), zeroOnFail) : null;
            var failed = counted.Count(l => !l.Passed);
            var overall = gpa is { } g && scale is not null
                ? scale.Bands.Where(b => b.GradePoint is not null && b.GradePoint <= g).OrderByDescending(b => b.GradePoint).FirstOrDefault() ?? scale.Bands.LastOrDefault()
                : scale?.BandFor(percent);
            rows.Add(new ResultRow(c.StudentId, c.AdmissionNo, c.Name, c.RollNo, c.Section.Id, c.Section.Label, c.Section.CampusId, c.Section.YearGroupId, percent,
                complete ? overall?.Grade : null, complete ? gpa : null, complete && failed == 0, failed, null, null, null,
                attendance.TryGetValue(c.StudentId, out var att) ? att : null, lines, complete));
        }
        return (Rank(rows), pending);
    }

    /// <summary>
    /// Section and class (class + campus) ranks by % with shared places for ties (1, 2, 2, 4), the percentile within the
    /// class, and each subject's highest % in the section. Incomplete results are not ranked.
    /// </summary>
    public static List<ResultRow> Rank(List<ResultRow> rows)
    {
        var complete = rows.Where(r => r.Complete).ToList();
        int? Place(ResultRow r, IEnumerable<ResultRow> group) => r.Complete ? group.Count(o => o.Percent > r.Percent) + 1 : null;
        var highest = complete.SelectMany(r => r.Subjects.Where(s => s.Percent is not null && s.Status != "EX").Select(s => (r.SectionId, s.SubjectId, s.Percent!.Value)))
            .GroupBy(x => (x.SectionId, x.SubjectId)).ToDictionary(g => g.Key, g => g.Max(x => x.Value));
        return rows.Select(r =>
        {
            var section = complete.Where(o => o.SectionId == r.SectionId);
            var cls = complete.Where(o => o.YearGroupId == r.YearGroupId && o.CampusId == r.CampusId).ToList();
            decimal? percentile = !r.Complete ? null : cls.Count <= 1 ? 100 : Math.Round(cls.Count(o => o.Percent < r.Percent) * 100m / (cls.Count - 1), 1);
            var subjects = r.Subjects.Select(s => s with { HighestInSection = highest.TryGetValue((r.SectionId, s.SubjectId), out var h) ? h : null }).ToList();
            return r with { SectionRank = Place(r, section), ClassRank = Place(r, cls), Percentile = percentile, Subjects = subjects };
        }).ToList();
    }

    private async Task<List<ResultRow>> StoredRowsAsync(Exam exam, CancellationToken ct)
    {
        var results = await db.ExamResults.AsNoTracking().Where(r => r.ExamId == exam.Id).ToListAsync(ct);
        var sections = await lookups.SectionsAsync(results.Select(r => r.SectionId), ct);
        var ids = results.Select(r => r.StudentId).ToList();
        var students = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.AdmissionNo, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName) }).ToDictionaryAsync(s => s.Id, ct);
        var rolls = await db.StudentEnrolments.IgnoreQueryFilters().AsNoTracking().Where(e => ids.Contains(e.StudentId) && e.AcademicYearId == exam.AcademicYearId && !e.IsDeleted)
            .GroupBy(e => e.StudentId).Select(g => new { g.Key, Roll = g.OrderByDescending(e => e.StartDate).Select(e => e.RollNo).FirstOrDefault() })
            .ToDictionaryAsync(x => x.Key, x => x.Roll, ct);
        return results.Select(r => new ResultRow(r.StudentId, students.GetValueOrDefault(r.StudentId)?.AdmissionNo ?? "?", students.GetValueOrDefault(r.StudentId)?.Name ?? "?",
            rolls.GetValueOrDefault(r.StudentId), r.SectionId, sections.GetValueOrDefault(r.SectionId)?.Label ?? "?", r.CampusId, r.YearGroupId, r.Percent, r.Grade, r.Gpa,
            r.Passed, r.FailedSubjects, r.SectionRank, r.ClassRank, r.Percentile, r.AttendancePercent,
            JsonSerializer.Deserialize<List<SubjectResult>>(r.SubjectsJson, Json) ?? [], true)).ToList();
    }

    /// <summary>Attendance % in the exam's term (or the year so far) up to the last exam day.</summary>
    private async Task<Dictionary<Guid, decimal>> AttendancePercentsAsync(Exam exam, List<Guid> studentIds, CancellationToken ct)
    {
        var from = exam.TermId is { } t
            ? await db.Terms.IgnoreQueryFilters().Where(x => x.Id == t).Select(x => x.StartDate).FirstAsync(ct)
            : await db.AcademicYears.IgnoreQueryFilters().Where(y => y.Id == exam.AcademicYearId).Select(y => y.StartDate).FirstAsync(ct);
        var to = exam.EndDate < lookups.TodayDhaka ? exam.EndDate : lookups.TodayDhaka;
        var rows = await db.StudentAttendances.IgnoreQueryFilters().AsNoTracking().Where(a => studentIds.Contains(a.StudentId) && a.Date >= from && a.Date <= to)
            .GroupBy(a => a.StudentId)
            .Select(g => new { g.Key, Marked = g.Count(), Attended = g.Count(a => a.Mark == AttendanceMark.Present || a.Mark == AttendanceMark.Late) }).ToListAsync(ct);
        return rows.Where(r => r.Marked > 0).ToDictionary(r => r.Key, r => SchoolDays.Percent(r.Attended, r.Marked));
    }

    private static decimal Pct(int part, int total) => total == 0 ? 0 : Math.Round(part * 100m / total, 1);

    private static string Summary(ExamResult r) =>
        (r.Gpa is { } g ? $"GPA {g:0.00}" : $"{r.Percent:0.##}%") + (r.Grade is null ? "" : $" ({r.Grade})") + (r.Passed ? ", passed" : ", not passed");

    private string Sign(ExamResult r)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(configuration["Documents:SigningKey"] ?? configuration["Jwt:SigningKey"] ?? "school-management"));
        var payload = $"{r.VerificationCode}|{r.ExamId:N}|{r.StudentId:N}|{r.Percent:F2}|{r.Grade}|{r.Gpa:F2}|{r.Passed}|{r.SubjectsJson}";
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
    }

    private async Task<string> NewCodeAsync(CancellationToken ct)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        while (true)
        {
            var code = new string(RandomNumberGenerator.GetItems<char>(alphabet, 10));
            if (!await db.ExamResults.IgnoreQueryFilters().AnyAsync(r => r.VerificationCode == code, ct) && !db.ExamResults.Local.Any(r => r.VerificationCode == code)) return code;
        }
    }

    private void EnsureManage(Exam exam)
    {
        var ok = exam.CampusId is { } c ? currentUser.HasPermission(Permissions.Exams.Manage, c)
            : currentUser.HasPermission(Permissions.Exams.Manage) && (currentUser.IsSystem || currentUser.HasAllCampusAccess);
        if (!ok) throw new ForbiddenException("Only the exam office can do this.");
    }

    private bool CanTeacherRemark(SectionInfo section) =>
        currentUser.HasPermission(Permissions.Exams.Manage, section.CampusId)
        || (currentUser.HasPermission(Permissions.Exams.Marks, section.CampusId) && section.ClassTeacherUserId is not null && section.ClassTeacherUserId == currentUser.UserId);

    // ------------------------------------------------------------------ rendering

    private const string ReportCss = """
        body{font-family:'Segoe UI','Noto Sans Bengali',Arial,sans-serif;color:#1f2937;margin:0}
        .page{max-width:190mm;margin:8mm auto;padding:6mm 8mm;border:1.5px solid #0b3d6e;border-radius:3mm;page-break-after:always;box-sizing:border-box}
        .page:last-child{page-break-after:auto}
        .head{display:flex;justify-content:space-between;align-items:flex-start;border-bottom:3px solid #0b3d6e;padding-bottom:3mm;margin-bottom:4mm}
        .school{font-size:17pt;font-weight:700;color:#0b3d6e}.bn{font-size:10pt;color:#334155}.doc{text-align:right;font-weight:700;color:#0b3d6e}
        table{border-collapse:collapse;width:100%}table.info td{padding:2px 6px;font-size:10pt}table.info td:nth-child(odd){color:#64748b;width:22%}
        table.marks{margin-top:4mm;font-size:9.5pt}table.marks th,table.marks td{border:1px solid #cbd5e1;padding:3px 6px;text-align:center}
        table.marks th{background:#eef2f7}table.marks td:first-child{text-align:left}
        .fail{color:#b91c1c;font-weight:600}.summary{margin-top:4mm;font-size:10.5pt}.summary b{font-size:12pt}
        .box{border:1px solid #cbd5e1;border-radius:2mm;padding:2mm 3mm;margin-top:3mm;font-size:9.5pt;min-height:9mm}
        .box .t{color:#64748b;font-size:8.5pt}
        .foot{display:flex;justify-content:space-between;align-items:flex-end;margin-top:8mm;font-size:9pt}
        .qr{text-align:center;font-size:7.5pt;color:#64748b}.qr img{width:24mm;height:24mm;display:block;margin:0 auto}
        h3{color:#0b3d6e;font-size:11pt;margin:5mm 0 1mm}.signs{display:flex;justify-content:space-between;margin-top:12mm;font-size:9pt}
        @media print{.page{margin:0 auto;border-width:1px}}
        """;

    private string RenderCard(Exam exam, string year, string school, string? schoolBn, string campus, Student student, string sectionLabel, int? roll, ExamResult r,
        GradeScale? scale, bool showRank, string? principal, string verifyUrl)
    {
        string E(string? s) => ExamService.E(s);
        var subjects = JsonSerializer.Deserialize<List<SubjectResult>>(r.SubjectsJson, Json) ?? [];
        var componentNames = subjects.SelectMany(s => s.Components.Select(c => c.Name)).Distinct().ToList();
        var showComponents = componentNames.Count > 1;
        var descriptors = scale?.ScaleType == Domain.Core.GradingScaleType.Descriptor;
        var gpa = scale?.UsesGpa == true;
        var png = Convert.ToBase64String(qr.Png(verifyUrl, 4));

        var sb = new StringBuilder();
        sb.Append($"""
            <div class="page"><div class="head"><div><div class="school">{E(school)}</div><div class="bn">{E(schoolBn)}</div><div>{E(campus)}</div></div>
            <div class="doc">PROGRESS REPORT<br><span class="bn">অগ্রগতি প্রতিবেদন</span><br>{E(exam.Name)}{(exam.NameBn is null ? "" : $"<br><span class=\"bn\">{E(exam.NameBn)}</span>")}<br>{E(year)}</div></div>
            <table class="info"><tr><td>Name / নাম</td><td><b>{E(student.FullName)}</b>{(student.NameBn is null ? "" : $"<br>{E(student.NameBn)}")}</td>
            <td>Admission no / ভর্তি নং</td><td>{E(student.AdmissionNo)}</td></tr>
            <tr><td>Class / শ্রেণি</td><td>{E(sectionLabel)}</td><td>Roll / রোল</td><td>{roll}</td></tr>
            <tr><td>Attendance / উপস্থিতি</td><td>{(r.AttendancePercent is { } a ? $"{a:0.#}%" : "—")}</td><td>Date of birth</td><td>{student.DateOfBirth:dd MMM yyyy}</td></tr></table>
            <table class="marks"><tr><th>Subject / বিষয়</th>
            """);
        if (showComponents) foreach (var c in componentNames) sb.Append($"<th>{E(c)}</th>");
        sb.Append("<th>%</th><th>Grade / গ্রেড</th>");
        if (gpa) sb.Append("<th>GP</th>");
        if (descriptors) sb.Append("<th>Descriptor / মূল্যায়ন</th>");
        sb.Append("<th>Highest / সর্বোচ্চ</th></tr>");
        foreach (var s in subjects)
        {
            sb.Append($"<tr><td>{E(s.Name)}</td>");
            if (showComponents)
                foreach (var name in componentNames)
                {
                    var c = s.Components.FirstOrDefault(x => x.Name == name);
                    sb.Append($"<td>{(c is null ? "" : c.Code ?? (c.Marks is { } m ? $"{m:0.##}/{c.MaxMarks:0.##}" : "—"))}</td>");
                }
            sb.Append($"<td>{(s.Status ?? s.Percent?.ToString("0.##") ?? "—")}</td><td class=\"{(s.Passed ? "" : "fail")}\">{E(s.Grade ?? s.Status)}</td>");
            if (gpa) sb.Append($"<td>{s.GradePoint:0.00}</td>");
            if (descriptors) sb.Append($"<td>{E(s.Descriptor)}{(s.DescriptorBn is null ? "" : $" / {E(s.DescriptorBn)}")}</td>");
            sb.Append($"<td>{s.HighestInSection:0.##}</td></tr>");
        }
        sb.Append("</table>");
        sb.Append($"<div class=\"summary\">Overall / সামগ্রিক: <b>{r.Percent:0.##}%</b> · Grade <b>{E(r.Grade)}</b>");
        if (r.Gpa is { } g) sb.Append($" · GPA <b>{g:0.00}</b>");
        sb.Append($" · Result / ফলাফল: <b class=\"{(r.Passed ? "" : "fail")}\">{(r.Passed ? "Passed / উত্তীর্ণ" : "Not passed / অনুত্তীর্ণ")}</b>");
        if (showRank && r.SectionRank is not null) sb.Append($" · Position in section {r.SectionRank}, in class {r.ClassRank} (percentile {r.Percentile:0.#})");
        sb.Append("</div>");
        sb.Append($"<div class=\"box\"><span class=\"t\">Conduct / আচরণ</span><br>{(r.Conduct is { } cd ? E(ConductText(cd)) : "")}</div>");
        sb.Append($"<div class=\"box\"><span class=\"t\">Co-curricular / সহপাঠ্যক্রমিক কার্যক্রম</span><br>{E(r.CoCurricular)}</div>");
        sb.Append($"<div class=\"box\"><span class=\"t\">Class teacher's remarks / শ্রেণি শিক্ষকের মন্তব্য</span><br>{E(r.TeacherRemark)}</div>");
        sb.Append($"<div class=\"box\"><span class=\"t\">Principal's remarks / অধ্যক্ষের মন্তব্য</span><br>{E(r.PrincipalRemark)}</div>");
        sb.Append($"""
            <div class="foot"><div>______________________<br>Class teacher / শ্রেণি শিক্ষক</div>
            <div class="qr"><img alt="QR" src="data:image/png;base64,{png}">Verify: {E(r.VerificationCode)}<br>Signed {E(r.Signature[..16])}</div>
            <div style="text-align:right">______________________<br>{(string.IsNullOrWhiteSpace(principal) ? "" : E(principal) + "<br>")}Principal / অধ্যক্ষ</div></div></div>
            """);
        return sb.ToString();
    }

    private static string ConductText(Conduct c) => c switch
    {
        Conduct.Excellent => "Excellent / চমৎকার",
        Conduct.Good => "Good / ভালো",
        Conduct.Satisfactory => "Satisfactory / সন্তোষজনক",
        _ => "Needs improvement / উন্নতি প্রয়োজন"
    };
}

/// <summary>EXM-06: publishes embargoed results when their time comes.</summary>
public sealed class ResultPublicationJob : IScheduledJob
{
    public string Name => "result-publication";
    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var results = services.GetRequiredService<ResultService>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        foreach (var exam in await db.Exams.Where(e => e.ResultStatus == ResultStatus.Scheduled && e.PublishAtUtc <= now).ToListAsync(ct))
            await results.PublishNowAsync(exam, ct);
    }
}

/// <summary>SIS-05 hook: the year's final exam suggests promote / conditional / repeat from the number of failed subjects.</summary>
public sealed class ExamResultPromotionRule(AppDbContext db, ISettingsService settings) : IPromotionRule
{
    public async Task<(PromotionOutcome? Outcome, string? Note)> EvaluateAsync(StudentEnrolment enrolment, CancellationToken ct)
    {
        var result = await db.ExamResults.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.StudentId == enrolment.StudentId && r.AcademicYearId == enrolment.AcademicYearId)
            .Join(db.Exams.IgnoreQueryFilters().Where(e => !e.IsDeleted && e.Type == ExamType.Final && e.ResultStatus != ResultStatus.NotReady), r => r.ExamId, e => e.Id,
                (r, e) => new { r.FailedSubjects, r.Percent, r.Gpa, e.Name, e.EndDate })
            .OrderByDescending(x => x.EndDate).FirstOrDefaultAsync(ct);
        if (result is null) return (null, null);
        var allowed = await settings.GetIntAsync(SettingKeys.ExamMaxFailedForPromotion, 2, enrolment.CampusId, ct);
        var score = result.Gpa is { } g ? $"GPA {g:0.00}" : $"{result.Percent:0.##}%";
        if (result.FailedSubjects > allowed) return (PromotionOutcome.Repeat, $"{result.Name}: {score}, failed {result.FailedSubjects} subject(s).");
        if (result.FailedSubjects > 0) return (PromotionOutcome.Conditional, $"{result.Name}: {score}, failed {result.FailedSubjects} subject(s).");
        return (null, $"{result.Name}: {score}, passed.");
    }
}
