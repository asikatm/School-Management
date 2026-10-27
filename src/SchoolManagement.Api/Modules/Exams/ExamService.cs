using System.Net;
using System.Text;
using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Api.Modules.Attendance;
using SchoolManagement.Api.Modules.Learning;
using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Exams;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Documents;
using SchoolManagement.Infrastructure.Storage;

namespace SchoolManagement.Api.Modules.Exams;

public record ExamClassRequest(Guid YearGroupId, Guid? GradingSchemeId);

public record ExamRequest(Guid AcademicYearId, Guid? TermId, Guid? CampusId, string Code, string Name, string? NameBn, ExamType Type, DateOnly StartDate,
    DateOnly EndDate, List<ExamClassRequest> Classes);

public record ExamClassView(Guid YearGroupId, string ClassName, Guid? GradingSchemeId, string? GradingScheme);

public record ExamView(Guid Id, Guid AcademicYearId, string AcademicYear, Guid? TermId, string? TermName, Guid? CampusId, string? CampusName, string Code, string Name,
    string? NameBn, ExamType Type, DateOnly StartDate, DateOnly EndDate, bool SchedulePublished, ResultStatus ResultStatus, DateTime? PublishAtUtc,
    DateTime? PublishedAtUtc, IReadOnlyList<ExamClassView> Classes, int Papers, int MarkSheets, int MarkSheetsApproved);

public record ComponentRequest(string Name, decimal MaxMarks, decimal WeightPercent, bool FromGradebook);

public record PaperRequest(Guid YearGroupId, Guid SubjectId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, List<ComponentRequest> Components);

public record ComponentView(Guid Id, string Name, decimal MaxMarks, decimal WeightPercent, int Sequence, bool FromGradebook);

public record PaperView(Guid Id, Guid YearGroupId, string ClassName, Guid SubjectId, string SubjectCode, string SubjectName, DateOnly Date, TimeOnly StartTime,
    TimeOnly EndTime, IReadOnlyList<ComponentView> Components, int Candidates);

public record ExamClash(DateOnly Date, string ClassName, string FirstSubject, string FirstTime, string SecondSubject, string SecondTime, int Students,
    IReadOnlyList<string> Examples);

public record SeatingRequest(Guid CampusId, List<Guid> RoomIds);

public record SeatRow(int SeatNo, Guid StudentId, string AdmissionNo, string Name, string ClassLabel);

public record InvigilatorView(DateOnly Date, Guid UserId, string Name);

public record RoomSeating(Guid RoomId, string RoomName, int Capacity, IReadOnlyList<SeatRow> Seats, IReadOnlyList<InvigilatorView> Invigilators);

public record SeatingView(Guid ExamId, Guid CampusId, int Candidates, int Seated, IReadOnlyList<DateOnly> Days, IReadOnlyList<RoomSeating> Rooms);

public record InvigilatorRequest(Guid CampusId, DateOnly Date, Guid RoomId, List<Guid> UserIds);

public record SeatLookup(Guid StudentId, string AdmissionNo, string Name, string ClassLabel, string? Room, int? SeatNo);

public record DateSheetPaper(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string SubjectCode, string SubjectName);

public record DateSheetClass(Guid YearGroupId, string ClassName, IReadOnlyList<DateSheetPaper> Papers);

public record DateSheet(Guid ExamId, string ExamName, string? ExamNameBn, ExamType Type, DateOnly StartDate, DateOnly EndDate, string? CampusName,
    IReadOnlyList<DateSheetClass> Classes);

public record PortalExam(Guid ExamId, string Name, string? NameBn, ExamType Type, DateOnly StartDate, DateOnly EndDate, string? Room, int? SeatNo,
    IReadOnlyList<DateSheetPaper> Papers);

/// <summary>A student who sits the exam: their section and the papers they take.</summary>
public sealed record Candidate(Guid StudentId, string AdmissionNo, string Name, int? RollNo, SectionInfo Section, IReadOnlyList<Guid> PaperIds);

/// <summary>
/// EXM-01/02: exams and their date sheets (with clash checks for elective combinations), publication to the portal and
/// website, seating plans with invigilators, seat lookup and admit cards with photo and QR.
/// </summary>
public sealed class ExamService(
    AppDbContext db,
    AcademicLookups lookups,
    ClassRoster roster,
    GuardianNotifier notifier,
    AttendanceService attendance,
    IQrCodeService qr,
    IFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    // ------------------------------------------------------------------ exams

    public async Task<List<ExamView>> ListAsync(Guid? academicYearId, ExamType? type, CancellationToken ct)
    {
        var q = db.Exams.AsNoTracking().Include(e => e.Classes).AsQueryable();
        if (academicYearId is not null) q = q.Where(e => e.AcademicYearId == academicYearId);
        if (type is not null) q = q.Where(e => e.Type == type);
        var rows = await q.OrderByDescending(e => e.StartDate).ToListAsync(ct);
        return await ViewsAsync(rows.Where(e => e.CampusId is null || currentUser.CanAccessCampus(e.CampusId.Value)).ToList(), ct);
    }

    public async Task<ExamView> GetAsync(Guid id, CancellationToken ct) => (await ViewsAsync([await LoadAsync(id, ct)], ct))[0];

    public async Task<ExamView> CreateAsync(ExamRequest r, CancellationToken ct)
    {
        EnsureManage(r.CampusId);
        await ValidateAsync(r, null, ct);
        var exam = new Exam { AcademicYearId = r.AcademicYearId, Code = r.Code.Trim().ToUpperInvariant(), Name = r.Name.Trim() };
        Apply(exam, r);
        foreach (var c in r.Classes) exam.Classes.Add(new ExamClass { ExamId = exam.Id, YearGroupId = c.YearGroupId, GradingSchemeId = c.GradingSchemeId });
        db.Exams.Add(exam);
        await db.SaveChangesAsync(ct);
        return await GetAsync(exam.Id, ct);
    }

    public async Task<ExamView> UpdateAsync(Guid id, ExamRequest r, CancellationToken ct)
    {
        var exam = await db.Exams.Include(e => e.Classes).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Exam", id);
        EnsureManage(exam.CampusId);
        EnsureManage(r.CampusId);
        if (exam.ResultStatus != ResultStatus.NotReady) throw new DomainException("Results are finalised; the exam can no longer be changed.", "results_finalised", isConflict: true);
        if (r.AcademicYearId != exam.AcademicYearId) throw new DomainException("An exam cannot move to another academic year.", "fixed_year");
        await ValidateAsync(r, id, ct);
        var removed = exam.Classes.Where(c => r.Classes.All(x => x.YearGroupId != c.YearGroupId)).ToList();
        foreach (var c in removed)
        {
            if (await db.ExamPapers.AnyAsync(p => p.ExamId == id && p.YearGroupId == c.YearGroupId, ct))
                throw new DomainException("Remove the class's papers from the date sheet before removing the class.", "class_has_papers", isConflict: true);
            db.ExamClasses.Remove(c);
        }
        foreach (var c in r.Classes)
        {
            var row = exam.Classes.FirstOrDefault(x => x.YearGroupId == c.YearGroupId);
            if (row is null) db.ExamClasses.Add(new ExamClass { ExamId = exam.Id, YearGroupId = c.YearGroupId, GradingSchemeId = c.GradingSchemeId });
            else row.GradingSchemeId = c.GradingSchemeId;
        }
        if (await db.ExamPapers.AnyAsync(p => p.ExamId == id && (p.Date < r.StartDate || p.Date > r.EndDate), ct))
            throw new DomainException("Some papers fall outside the new dates; move them first.", "papers_outside", isConflict: true);
        exam.Code = r.Code.Trim().ToUpperInvariant();
        Apply(exam, r);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var exam = await LoadAsync(id, ct, tracked: true);
        EnsureManage(exam.CampusId);
        if (await db.ExamMarks.AnyAsync(m => db.ExamPapers.Any(p => p.Id == m.ExamPaperId && p.ExamId == id), ct))
            throw new DomainException("Marks have been entered for this exam; it cannot be deleted.", "has_marks", isConflict: true);
        db.Exams.Remove(exam);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ date sheet

    public async Task<List<PaperView>> PapersAsync(Guid examId, CancellationToken ct)
    {
        var exam = await LoadAsync(examId, ct);
        var papers = await db.ExamPapers.AsNoTracking().Include(p => p.Components).Where(p => p.ExamId == examId).ToListAsync(ct);
        var candidates = await CandidatesAsync(exam, ct);
        return await PaperViewsAsync(papers, candidates, ct);
    }

    public async Task<PaperView> AddPaperAsync(Guid examId, PaperRequest r, CancellationToken ct)
    {
        var exam = await LoadAsync(examId, ct);
        EnsureManage(exam.CampusId);
        EnsureScheduleEditable(exam);
        ValidatePaper(exam, r);
        if (await db.ExamPapers.AnyAsync(p => p.ExamId == examId && p.YearGroupId == r.YearGroupId && p.SubjectId == r.SubjectId, ct))
            throw new DomainException("This class already has a paper in that subject.", "duplicate_paper", isConflict: true);
        var paper = new ExamPaper { ExamId = examId, YearGroupId = r.YearGroupId, SubjectId = r.SubjectId, Date = r.Date, StartTime = r.StartTime, EndTime = r.EndTime };
        var seq = 0;
        foreach (var c in r.Components)
            paper.Components.Add(new ExamPaperComponent { ExamPaperId = paper.Id, Name = c.Name.Trim(), MaxMarks = c.MaxMarks, WeightPercent = c.WeightPercent, Sequence = seq++, FromGradebook = c.FromGradebook });
        db.ExamPapers.Add(paper);
        await db.SaveChangesAsync(ct);
        return (await PaperViewsAsync([paper], await CandidatesAsync(exam, ct), ct))[0];
    }

    public async Task<PaperView> UpdatePaperAsync(Guid paperId, PaperRequest r, CancellationToken ct)
    {
        var paper = await db.ExamPapers.Include(p => p.Components).FirstOrDefaultAsync(p => p.Id == paperId, ct) ?? throw new NotFoundException("Paper", paperId);
        var exam = await LoadAsync(paper.ExamId, ct);
        EnsureManage(exam.CampusId);
        EnsureScheduleEditable(exam);
        ValidatePaper(exam, r);
        if (r.YearGroupId != paper.YearGroupId || r.SubjectId != paper.SubjectId)
            throw new DomainException("Class and subject of a paper cannot change; remove it and add a new one.", "fixed_paper");
        paper.Date = r.Date;
        paper.StartTime = r.StartTime;
        paper.EndTime = r.EndTime;

        var withMarks = (await db.ExamMarks.Where(m => m.ExamPaperId == paperId).Select(m => m.ComponentId).Distinct().ToListAsync(ct)).ToHashSet();
        foreach (var gone in paper.Components.Where(c => r.Components.All(x => !x.Name.Trim().Equals(c.Name, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            if (withMarks.Contains(gone.Id)) throw new DomainException($"'{gone.Name}' already has marks and cannot be removed.", "component_has_marks", isConflict: true);
            db.ExamPaperComponents.Remove(gone);
        }
        var seq = 0;
        foreach (var c in r.Components)
        {
            var row = paper.Components.FirstOrDefault(x => x.Name.Equals(c.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new ExamPaperComponent { ExamPaperId = paper.Id, Name = c.Name.Trim() };
                db.ExamPaperComponents.Add(row);
            }
            else if (withMarks.Contains(row.Id) && row.MaxMarks != c.MaxMarks && await db.MarkSheets.AnyAsync(s => s.ExamPaperId == paperId && s.Status == MarkSheetStatus.Approved, ct))
                throw new DomainException($"Marks for '{row.Name}' are approved; the maximum cannot change.", "approved_marks", isConflict: true);
            row.MaxMarks = c.MaxMarks;
            row.WeightPercent = c.WeightPercent;
            row.FromGradebook = c.FromGradebook;
            row.Sequence = seq++;
        }
        await db.SaveChangesAsync(ct);
        return (await PaperViewsAsync([paper], await CandidatesAsync(exam, ct), ct))[0];
    }

    public async Task DeletePaperAsync(Guid paperId, CancellationToken ct)
    {
        var paper = await db.ExamPapers.Include(p => p.Components).FirstOrDefaultAsync(p => p.Id == paperId, ct) ?? throw new NotFoundException("Paper", paperId);
        var exam = await LoadAsync(paper.ExamId, ct);
        EnsureManage(exam.CampusId);
        EnsureScheduleEditable(exam);
        if (await db.ExamMarks.AnyAsync(m => m.ExamPaperId == paperId, ct)) throw new DomainException("Marks have been entered for this paper.", "has_marks", isConflict: true);
        db.MarkSheets.RemoveRange(await db.MarkSheets.Where(s => s.ExamPaperId == paperId).ToListAsync(ct));
        db.ExamPapers.Remove(paper);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>EXM-01: papers of the same class that overlap in time and share at least one student (elective combinations included).</summary>
    public async Task<List<ExamClash>> ClashesAsync(Guid examId, CancellationToken ct)
    {
        var exam = await LoadAsync(examId, ct);
        var papers = await db.ExamPapers.AsNoTracking().Where(p => p.ExamId == examId).ToListAsync(ct);
        var candidates = await CandidatesAsync(exam, ct);
        var takers = papers.ToDictionary(p => p.Id, p => candidates.Where(c => c.PaperIds.Contains(p.Id)).ToList());
        var subjects = await lookups.SubjectsAsync(ct);
        var classes = await ClassNamesAsync(ct);
        var result = new List<ExamClash>();
        foreach (var day in papers.GroupBy(p => (p.Date, p.YearGroupId)))
        {
            var list = day.OrderBy(p => p.StartTime).ToList();
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                {
                    var (a, b) = (list[i], list[j]);
                    if (!AcademicLookups.Overlaps(a.StartTime, a.EndTime, b.StartTime, b.EndTime)) continue;
                    var both = takers[a.Id].Select(c => c.StudentId).Intersect(takers[b.Id].Select(c => c.StudentId)).ToHashSet();
                    if (both.Count == 0) continue;
                    result.Add(new ExamClash(a.Date, classes.GetValueOrDefault(a.YearGroupId, "?"), subjects.GetValueOrDefault(a.SubjectId).Name, $"{a.StartTime:HH:mm}–{a.EndTime:HH:mm}",
                        subjects.GetValueOrDefault(b.SubjectId).Name, $"{b.StartTime:HH:mm}–{b.EndTime:HH:mm}", both.Count,
                        takers[a.Id].Where(c => both.Contains(c.StudentId)).Take(5).Select(c => $"{c.Name} ({c.AdmissionNo})").ToList()));
                }
        }
        return result.OrderBy(c => c.Date).ThenBy(c => c.ClassName).ToList();
    }

    /// <summary>Publishes the date sheet to the portal and website and tells the guardians (Appendix C: exam schedule / admit card ready).</summary>
    public async Task<ExamView> PublishScheduleAsync(Guid examId, bool publish, CancellationToken ct)
    {
        var exam = await LoadAsync(examId, ct, tracked: true);
        EnsureManage(exam.CampusId);
        if (publish && exam.SchedulePublished) throw new DomainException("The date sheet is already published.", "already_published", isConflict: true);
        if (publish)
        {
            if (!await db.ExamPapers.AnyAsync(p => p.ExamId == examId, ct)) throw new DomainException("Add papers to the date sheet first.", "no_papers");
            if ((await ClashesAsync(examId, ct)).Count > 0) throw new DomainException("Resolve the timetable clashes before publishing.", "clashes", isConflict: true);
            var wasEverPublished = exam.SchedulePublishedAtUtc is not null;
            exam.SchedulePublished = true;
            exam.SchedulePublishedAtUtc = clock.GetUtcNow().UtcDateTime;
            if (!wasEverPublished)
                foreach (var c in (await CandidatesAsync(exam, ct)).DistinctBy(c => c.StudentId))
                    await notifier.NotifyAsync(c.StudentId, c.Section.CampusId, TemplateCodes.ExamSchedulePublished, new Dictionary<string, string?>
                    {
                        ["ChildName"] = c.Name, ["ClassName"] = c.Section.Label, ["Exam"] = exam.Name, ["Start"] = exam.StartDate.ToString("dd MMM yyyy")
                    }, nameof(Exam), exam.Id.ToString(), ct, includeStudent: true);
        }
        else exam.SchedulePublished = false;
        await db.SaveChangesAsync(ct);
        return await GetAsync(examId, ct);
    }

    public async Task<DateSheet> DateSheetAsync(Guid examId, CancellationToken ct)
    {
        var exam = await db.Exams.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && !e.IsDeleted, ct) ?? throw new NotFoundException("Exam", examId);
        return (await DateSheetsAsync([exam], null, ct))[0];
    }

    /// <summary>Published date sheets (website and portal). <paramref name="yearGroupId"/> narrows to one class.</summary>
    public async Task<List<DateSheet>> PublishedDateSheetsAsync(Guid? yearGroupId, CancellationToken ct)
    {
        var today = lookups.TodayDhaka;
        var exams = await db.Exams.IgnoreQueryFilters().AsNoTracking()
            .Where(e => !e.IsDeleted && e.SchedulePublished && e.EndDate >= today.AddDays(-7)).OrderBy(e => e.StartDate).ToListAsync(ct);
        return (await DateSheetsAsync(exams, yearGroupId, ct)).Where(d => d.Classes.Count > 0).ToList();
    }

    /// <summary>Portal: the published exams a student sits, with their own papers and seat.</summary>
    public async Task<List<PortalExam>> ForStudentAsync(Guid studentId, CancellationToken ct)
    {
        var today = lookups.TodayDhaka;
        var exams = await db.Exams.IgnoreQueryFilters().AsNoTracking()
            .Where(e => !e.IsDeleted && e.SchedulePublished && e.EndDate >= today.AddDays(-30)).OrderBy(e => e.StartDate).ToListAsync(ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var rooms = await lookups.RoomNamesAsync(ct);
        var result = new List<PortalExam>();
        foreach (var exam in exams)
        {
            var me = (await CandidatesAsync(exam, ct)).FirstOrDefault(c => c.StudentId == studentId);
            if (me is null) continue;
            var papers = await db.ExamPapers.AsNoTracking().Where(p => me.PaperIds.Contains(p.Id)).OrderBy(p => p.Date).ThenBy(p => p.StartTime).ToListAsync(ct);
            var seat = await db.ExamSeats.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.ExamId == exam.Id && s.StudentId == studentId, ct);
            result.Add(new PortalExam(exam.Id, exam.Name, exam.NameBn, exam.Type, exam.StartDate, exam.EndDate, seat is null ? null : rooms.GetValueOrDefault(seat.RoomId), seat?.SeatNo,
                papers.Select(p => new DateSheetPaper(p.Date, p.StartTime, p.EndTime, subjects.GetValueOrDefault(p.SubjectId).Code, subjects.GetValueOrDefault(p.SubjectId).Name)).ToList()));
        }
        return result;
    }

    // ------------------------------------------------------------------ seating & invigilation (EXM-02)

    public async Task<SeatingView> SeatingAsync(Guid examId, Guid campusId, CancellationToken ct)
    {
        var exam = await LoadAsync(examId, ct);
        EnsureCampus(campusId);
        var candidates = (await CandidatesAsync(exam, ct)).Where(c => c.Section.CampusId == campusId).DistinctBy(c => c.StudentId).ToDictionary(c => c.StudentId);
        var seats = await db.ExamSeats.AsNoTracking().Where(s => s.ExamId == examId && s.CampusId == campusId).ToListAsync(ct);
        var invigilators = await db.ExamInvigilators.AsNoTracking().Where(i => i.ExamId == examId && i.CampusId == campusId).ToListAsync(ct);
        var roomIds = seats.Select(s => s.RoomId).Concat(invigilators.Select(i => i.RoomId)).Distinct().ToList();
        var rooms = await db.Rooms.IgnoreQueryFilters().AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var names = await lookups.UserNamesAsync(invigilators.Select(i => (Guid?)i.UserId), ct);
        var students = await StudentBasicsAsync(seats.Select(s => s.StudentId), ct);
        var days = await db.ExamPapers.AsNoTracking().Where(p => p.ExamId == examId).Select(p => p.Date).Distinct().OrderBy(d => d).ToListAsync(ct);

        var result = roomIds.Where(rooms.ContainsKey).OrderBy(id => rooms[id].Code).Select(id => new RoomSeating(id, rooms[id].Name, rooms[id].Capacity,
            seats.Where(s => s.RoomId == id).OrderBy(s => s.SeatNo).Select(s =>
            {
                var st = students.GetValueOrDefault(s.StudentId);
                return new SeatRow(s.SeatNo, s.StudentId, st?.AdmissionNo ?? "?", st?.Name ?? "?", candidates.GetValueOrDefault(s.StudentId)?.Section.Label ?? "—");
            }).ToList(),
            invigilators.Where(i => i.RoomId == id).OrderBy(i => i.Date).Select(i => new InvigilatorView(i.Date, i.UserId, names.GetValueOrDefault(i.UserId, "?"))).ToList()))
            .ToList();
        return new SeatingView(examId, campusId, candidates.Count, seats.Count(s => candidates.ContainsKey(s.StudentId)), days, result);
    }

    /// <summary>
    /// Seats every candidate of the campus in the chosen rooms, in order, up to each room's capacity. Students are
    /// interleaved by class and section so neighbours sit different papers or at least come from different sections.
    /// </summary>
    public async Task<SeatingView> GenerateSeatingAsync(Guid examId, SeatingRequest r, CancellationToken ct)
    {
        var exam = await LoadAsync(examId, ct);
        EnsureManage(r.CampusId);
        var rooms = await db.Rooms.AsNoTracking().Where(x => r.RoomIds.Contains(x.Id) && x.CampusId == r.CampusId && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        if (rooms.Count != r.RoomIds.Distinct().Count()) throw new DomainException("Choose active rooms of this campus.", "bad_rooms");
        var candidates = (await CandidatesAsync(exam, ct)).Where(c => c.Section.CampusId == r.CampusId).DistinctBy(c => c.StudentId).ToList();
        if (candidates.Count == 0) throw new DomainException("No students on this campus sit this exam.", "no_candidates");
        var capacity = r.RoomIds.Distinct().Sum(id => rooms[id].Capacity);
        if (capacity < candidates.Count) throw new DomainException($"The rooms seat {capacity}, but {candidates.Count} students sit the exam. Add rooms.", "not_enough_seats");

        db.ExamSeats.RemoveRange(await db.ExamSeats.Where(s => s.ExamId == examId && s.CampusId == r.CampusId).ToListAsync(ct));
        var order = Interleave(candidates);
        var index = 0;
        foreach (var roomId in r.RoomIds.Distinct())
            for (var seat = 1; seat <= rooms[roomId].Capacity && index < order.Count; seat++)
                db.ExamSeats.Add(new ExamSeat { CampusId = r.CampusId, ExamId = examId, StudentId = order[index++].StudentId, RoomId = roomId, SeatNo = seat });
        await db.SaveChangesAsync(ct);
        return await SeatingAsync(examId, r.CampusId, ct);
    }

    /// <summary>Round-robin over sections so consecutive seats go to different sections (and, across classes, different papers).</summary>
    public static List<Candidate> Interleave(IReadOnlyList<Candidate> candidates)
    {
        var queues = candidates.GroupBy(c => c.Section.Id).OrderBy(g => g.First().Section.Label)
            .Select(g => new Queue<Candidate>(g.OrderBy(c => c.RollNo ?? int.MaxValue).ThenBy(c => c.Name))).ToList();
        var result = new List<Candidate>(candidates.Count);
        while (queues.Any(q => q.Count > 0))
            foreach (var q in queues.Where(q => q.Count > 0))
                result.Add(q.Dequeue());
        return result;
    }

    public async Task<SeatingView> SetInvigilatorsAsync(Guid examId, InvigilatorRequest r, CancellationToken ct)
    {
        await LoadAsync(examId, ct);
        EnsureManage(r.CampusId);
        db.ExamInvigilators.RemoveRange(await db.ExamInvigilators.Where(i => i.ExamId == examId && i.CampusId == r.CampusId && i.Date == r.Date && i.RoomId == r.RoomId).ToListAsync(ct));
        foreach (var userId in r.UserIds.Distinct())
        {
            if (await db.ExamInvigilators.AnyAsync(i => i.ExamId == examId && i.Date == r.Date && i.UserId == userId && i.RoomId != r.RoomId, ct))
                throw new DomainException("That teacher already invigilates another room on this day.", "invigilator_busy", isConflict: true);
            db.ExamInvigilators.Add(new ExamInvigilator { CampusId = r.CampusId, ExamId = examId, Date = r.Date, RoomId = r.RoomId, UserId = userId });
        }
        await db.SaveChangesAsync(ct);
        return await SeatingAsync(examId, r.CampusId, ct);
    }

    /// <summary>One invigilator per seated room per exam day, spread evenly over the campus's teachers who are not on leave.</summary>
    public async Task<SeatingView> AutoInvigilatorsAsync(Guid examId, Guid campusId, CancellationToken ct)
    {
        await LoadAsync(examId, ct);
        EnsureManage(campusId);
        var roomIds = await db.ExamSeats.AsNoTracking().Where(s => s.ExamId == examId && s.CampusId == campusId).Select(s => s.RoomId).Distinct().ToListAsync(ct);
        if (roomIds.Count == 0) throw new DomainException("Generate the seating plan first.", "no_seating");
        var days = await db.ExamPapers.AsNoTracking().Where(p => p.ExamId == examId).Select(p => p.Date).Distinct().OrderBy(d => d).ToListAsync(ct);
        var roles = new[] { RoleNames.ClassTeacher, RoleNames.SubjectTeacher, RoleNames.HeadOfSection };
        var teachers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.UserType == UserType.Staff && u.RoleAssignments.Any(a => roles.Contains(a.Role!.Name) && (a.CampusId == null || a.CampusId == campusId)))
            .OrderBy(u => u.FullName).Select(u => u.Id).ToListAsync(ct);
        if (teachers.Count == 0) throw new DomainException("No teachers are assigned to this campus.", "no_teachers");

        db.ExamInvigilators.RemoveRange(await db.ExamInvigilators.Where(i => i.ExamId == examId && i.CampusId == campusId).ToListAsync(ct));
        var load = teachers.ToDictionary(t => t, _ => 0);
        foreach (var day in days)
        {
            var away = (await Attendance.StaffLeaveService.OnLeave(db, day).ToListAsync(ct)).ToHashSet();
            var free = teachers.Where(t => !away.Contains(t)).ToList();
            foreach (var room in roomIds)
            {
                var pick = free.OrderBy(t => load[t]).FirstOrDefault();
                if (pick == Guid.Empty) break;
                free.Remove(pick);
                load[pick]++;
                db.ExamInvigilators.Add(new ExamInvigilator { CampusId = campusId, ExamId = examId, Date = day, RoomId = room, UserId = pick });
            }
        }
        await db.SaveChangesAsync(ct);
        return await SeatingAsync(examId, campusId, ct);
    }

    public async Task<List<SeatLookup>> LookupSeatAsync(Guid examId, string query, CancellationToken ct)
    {
        var exam = await LoadAsync(examId, ct);
        var q = query.Trim();
        var matches = (await CandidatesAsync(exam, ct)).DistinctBy(c => c.StudentId)
            .Where(c => c.AdmissionNo.Equals(q, StringComparison.OrdinalIgnoreCase) || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();
        var ids = matches.Select(m => m.StudentId).ToList();
        var seats = await db.ExamSeats.AsNoTracking().Where(s => s.ExamId == examId && ids.Contains(s.StudentId)).ToDictionaryAsync(s => s.StudentId, ct);
        var rooms = await lookups.RoomNamesAsync(ct);
        return matches.Select(m =>
        {
            var s = seats.GetValueOrDefault(m.StudentId);
            return new SeatLookup(m.StudentId, m.AdmissionNo, m.Name, m.Section.Label, s is null ? null : rooms.GetValueOrDefault(s.RoomId), s?.SeatNo);
        }).ToList();
    }

    /// <summary>EXM-02: printable admit cards (photo, QR, seat and the student's own papers) for a campus, a section or one student.</summary>
    public async Task<string> AdmitCardsAsync(Guid examId, Guid? campusId, Guid? sectionId, Guid? studentId, CancellationToken ct)
    {
        var exam = await db.Exams.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId && !e.IsDeleted, ct) ?? throw new NotFoundException("Exam", examId);
        var candidates = (await CandidatesAsync(exam, ct)).DistinctBy(c => c.StudentId)
            .Where(c => (campusId == null || c.Section.CampusId == campusId) && (sectionId == null || c.Section.Id == sectionId) && (studentId == null || c.StudentId == studentId))
            .OrderBy(c => c.Section.Label).ThenBy(c => c.RollNo ?? int.MaxValue).ThenBy(c => c.Name).ToList();
        if (candidates.Count == 0) throw new DomainException("No students match.", "no_candidates");
        if (studentId is null) foreach (var c in candidates.Select(c => c.Section.CampusId).Distinct()) EnsureCampus(c);

        var papers = await db.ExamPapers.AsNoTracking().Where(p => p.ExamId == examId).ToDictionaryAsync(p => p.Id, ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var ids = candidates.Select(c => c.StudentId).ToList();
        var seats = await db.ExamSeats.IgnoreQueryFilters().AsNoTracking().Where(s => s.ExamId == examId && ids.Contains(s.StudentId)).ToDictionaryAsync(s => s.StudentId, ct);
        var rooms = await lookups.RoomNamesAsync(ct);
        var photos = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.Id) && s.PhotoPath != null).ToDictionaryAsync(s => s.Id, s => s.PhotoPath!, ct);
        var campuses = await db.Campuses.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var school = await db.Organisations.IgnoreQueryFilters().AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "";
        var lowAttendance = await LowAttendanceAsync(exam, candidates, ct);

        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html><html><head><meta charset="utf-8"><title>Admit cards — {{E(exam.Name)}}</title><style>
            body{font-family:'Segoe UI','Noto Sans Bengali',Arial,sans-serif;color:#1f2937;margin:10mm}
            .card{border:1.5px solid #0b3d6e;border-radius:3mm;padding:5mm 6mm;margin-bottom:8mm;break-inside:avoid;position:relative}
            .head{display:flex;justify-content:space-between;align-items:center;border-bottom:2px solid #0b3d6e;padding-bottom:2mm;margin-bottom:3mm}
            .school{font-weight:700;font-size:15pt;color:#0b3d6e}.title{font-weight:600;font-size:11pt}
            .body{display:flex;gap:5mm}.photo{width:26mm;height:32mm;border:1px solid #cbd5e1;object-fit:cover;display:flex;align-items:center;justify-content:center;color:#94a3b8;font-size:8pt}
            table.info td{padding:1px 8px 1px 0;font-size:10pt}table.info td:first-child{color:#64748b}
            table.papers{border-collapse:collapse;width:100%;margin-top:3mm;font-size:9pt}table.papers th,table.papers td{border:1px solid #cbd5e1;padding:2px 6px;text-align:left}
            table.papers th{background:#f1f5f9}.qr{width:24mm;height:24mm;margin-left:auto}
            .warn{margin-top:2mm;padding:2mm 3mm;background:#fef2f2;border:1px solid #fecaca;color:#991b1b;font-size:9pt}
            .sign{display:flex;justify-content:space-between;margin-top:8mm;font-size:9pt;color:#475569}
            @media print{ body{margin:6mm} .card{page-break-inside:avoid} }
            </style></head><body>
            """);
        foreach (var c in candidates)
        {
            var seat = seats.GetValueOrDefault(c.StudentId);
            var photo = photos.TryGetValue(c.StudentId, out var path) ? await PhotoDataUriAsync(path, ct) : null;
            var png = Convert.ToBase64String(qr.Png($"SMS-ADMIT:{exam.Code}:{c.AdmissionNo}", 4));
            sb.Append($"""
                <div class="card"><div class="head"><div><div class="school">{E(school)}</div><div>{E(campuses.GetValueOrDefault(c.Section.CampusId))}</div></div>
                <div class="title">ADMIT CARD · প্রবেশপত্র<br>{E(exam.Name)}</div></div>
                <div class="body">{(photo is null ? "<div class=\"photo\">Photo</div>" : $"<img class=\"photo\" src=\"{photo}\" alt=\"Photo\">")}
                <table class="info"><tr><td>Name</td><td><b>{E(c.Name)}</b></td></tr><tr><td>Admission no</td><td>{E(c.AdmissionNo)}</td></tr>
                <tr><td>Class</td><td>{E(c.Section.Label)}</td></tr><tr><td>Roll</td><td>{c.RollNo}</td></tr>
                <tr><td>Room / seat</td><td><b>{(seat is null ? "To be announced" : $"{E(rooms.GetValueOrDefault(seat.RoomId))} · seat {seat.SeatNo}")}</b></td></tr></table>
                <img class="qr" alt="QR" src="data:image/png;base64,{png}"></div>
                <table class="papers"><tr><th>Date</th><th>Time</th><th>Subject</th></tr>
                """);
            foreach (var p in c.PaperIds.Select(id => papers[id]).OrderBy(p => p.Date).ThenBy(p => p.StartTime))
                sb.Append($"<tr><td>{p.Date:ddd, dd MMM yyyy}</td><td>{p.StartTime:hh\\:mm tt}–{p.EndTime:hh\\:mm tt}</td><td>{E(subjects.GetValueOrDefault(p.SubjectId).Name)}</td></tr>");
            sb.Append("</table>");
            if (lowAttendance.TryGetValue(c.StudentId, out var pct))
                sb.Append($"<div class=\"warn\">Attendance {pct:0.#}% is below the examination minimum. Eligibility must be confirmed by the principal.</div>");
            sb.Append("<div class=\"sign\"><span>Bring this card to every paper. No electronic devices.</span><span>______________________<br>Controller of Examinations</span></div></div>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ shared with marks and results

    /// <summary>
    /// Everyone who sits the exam: students of the exam's classes (and campus) enrolled when it starts, with the papers of
    /// their class in subjects they take.
    /// </summary>
    public async Task<List<Candidate>> CandidatesAsync(Exam exam, CancellationToken ct)
    {
        var classIds = await db.ExamClasses.AsNoTracking().Where(c => c.ExamId == exam.Id).Select(c => c.YearGroupId).ToListAsync(ct);
        var papers = await db.ExamPapers.AsNoTracking().Where(p => p.ExamId == exam.Id).Select(p => new { p.Id, p.YearGroupId, p.SubjectId }).ToListAsync(ct);
        var sectionIds = await db.Sections.IgnoreQueryFilters().AsNoTracking()
            .Where(s => !s.IsDeleted && s.AcademicYearId == exam.AcademicYearId && classIds.Contains(s.YearGroupId) && (exam.CampusId == null || s.CampusId == exam.CampusId))
            .Select(s => s.Id).ToListAsync(ct);
        var sections = await lookups.SectionsAsync(sectionIds, ct);
        var on = exam.StartDate < lookups.TodayDhaka ? exam.StartDate : lookups.TodayDhaka;

        var result = new List<Candidate>();
        foreach (var section in sections.Values)
        {
            var students = await roster.OfSectionAsync(section.Id, on, ct);
            if (students.Count == 0 && on != lookups.TodayDhaka) students = await roster.OfSectionAsync(section.Id, lookups.TodayDhaka, ct);
            if (students.Count == 0) continue;
            var ids = students.Select(s => s.StudentId).ToList();
            var taking = new Dictionary<Guid, HashSet<Guid>>();
            foreach (var p in papers.Where(p => p.YearGroupId == section.YearGroupId))
                taking[p.Id] = await roster.TakersAsync(section.YearGroupId, section.AcademicYearId, p.SubjectId, ids, ct);
            result.AddRange(students.Select(s => new Candidate(s.StudentId, s.AdmissionNo, s.Name, s.RollNo, section,
                taking.Where(t => t.Value.Contains(s.StudentId)).Select(t => t.Key).ToList())));
        }
        return result;
    }

    public async Task<Exam> LoadAsync(Guid id, CancellationToken ct, bool tracked = false)
    {
        var q = tracked ? db.Exams : db.Exams.AsNoTracking();
        var exam = await q.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Exam", id);
        if (exam.CampusId is { } c) EnsureCampus(c);
        return exam;
    }

    public async Task<Dictionary<Guid, string>> ClassNamesAsync(CancellationToken ct) =>
        await db.YearGroups.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(y => y.Id, y => y.Name, ct);

    public static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public async Task<string?> PhotoDataUriAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = storage.OpenRead(path);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var type = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" : "image/jpeg";
            return $"data:{type};base64,{Convert.ToBase64String(ms.ToArray())}";
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ internals

    private async Task<Dictionary<Guid, decimal>> LowAttendanceAsync(Exam exam, IReadOnlyList<Candidate> candidates, CancellationToken ct)
    {
        if (exam.TermId is null) return [];
        var result = new Dictionary<Guid, decimal>();
        foreach (var campusId in candidates.Select(c => c.Section.CampusId).Distinct())
        {
            try
            {
                var report = await attendance.ThresholdsAsync(exam.TermId, campusId, null, true, ct);
                foreach (var row in report.Students.Where(r => r.Level == Domain.Attendance.WarningLevel.BelowMinimum && r.Marked >= AttendanceWarningJob.MinimumMarkedDays))
                    result[row.StudentId] = row.Percent;
            }
            catch (DomainException)
            {
            }
            catch (ForbiddenException)
            {
            }
        }
        return result;
    }

    private async Task<List<DateSheet>> DateSheetsAsync(IReadOnlyList<Exam> exams, Guid? yearGroupId, CancellationToken ct)
    {
        var ids = exams.Select(e => e.Id).ToList();
        var papers = await db.ExamPapers.AsNoTracking().Where(p => ids.Contains(p.ExamId) && (yearGroupId == null || p.YearGroupId == yearGroupId)).ToListAsync(ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var classes = await ClassNamesAsync(ct);
        var campuses = await db.Campuses.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return exams.Select(e => new DateSheet(e.Id, e.Name, e.NameBn, e.Type, e.StartDate, e.EndDate, e.CampusId is { } c ? campuses.GetValueOrDefault(c) : null,
            papers.Where(p => p.ExamId == e.Id).GroupBy(p => p.YearGroupId).OrderBy(g => classes.GetValueOrDefault(g.Key))
                .Select(g => new DateSheetClass(g.Key, classes.GetValueOrDefault(g.Key, "?"), g.OrderBy(p => p.Date).ThenBy(p => p.StartTime)
                    .Select(p => new DateSheetPaper(p.Date, p.StartTime, p.EndTime, subjects.GetValueOrDefault(p.SubjectId).Code, subjects.GetValueOrDefault(p.SubjectId).Name)).ToList()))
                .ToList())).ToList();
    }

    private async Task<Dictionary<Guid, (string AdmissionNo, string Name)?>> StudentBasicsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        var rows = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => list.Contains(s.Id))
            .Select(s => new { s.Id, s.AdmissionNo, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName) }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => ((string, string)?)(r.AdmissionNo, r.Name));
    }

    private async Task<List<PaperView>> PaperViewsAsync(IReadOnlyList<ExamPaper> papers, IReadOnlyList<Candidate> candidates, CancellationToken ct)
    {
        var subjects = await lookups.SubjectsAsync(ct);
        var classes = await ClassNamesAsync(ct);
        return papers.OrderBy(p => p.Date).ThenBy(p => p.StartTime).ThenBy(p => classes.GetValueOrDefault(p.YearGroupId))
            .Select(p => new PaperView(p.Id, p.YearGroupId, classes.GetValueOrDefault(p.YearGroupId, "?"), p.SubjectId, subjects.GetValueOrDefault(p.SubjectId).Code ?? "?",
                subjects.GetValueOrDefault(p.SubjectId).Name ?? "?", p.Date, p.StartTime, p.EndTime,
                p.Components.OrderBy(c => c.Sequence).Select(c => new ComponentView(c.Id, c.Name, c.MaxMarks, c.WeightPercent, c.Sequence, c.FromGradebook)).ToList(),
                candidates.Count(c => c.PaperIds.Contains(p.Id)))).ToList();
    }

    private async Task<List<ExamView>> ViewsAsync(IReadOnlyList<Exam> exams, CancellationToken ct)
    {
        var ids = exams.Select(e => e.Id).ToList();
        var years = await db.AcademicYears.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(y => y.Id, y => y.Name, ct);
        var terms = await db.Terms.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var campuses = await db.Campuses.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var classes = await ClassNamesAsync(ct);
        var schemes = await db.GradingSchemes.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var classRows = await db.ExamClasses.AsNoTracking().Where(c => ids.Contains(c.ExamId)).ToListAsync(ct);
        var papers = await db.ExamPapers.AsNoTracking().Where(p => ids.Contains(p.ExamId)).GroupBy(p => p.ExamId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var sheets = await db.MarkSheets.IgnoreQueryFilters().AsNoTracking()
            .Join(db.ExamPapers.Where(p => ids.Contains(p.ExamId)), s => s.ExamPaperId, p => p.Id, (s, p) => new { p.ExamId, s.Status }).ToListAsync(ct);
        return exams.Select(e => new ExamView(e.Id, e.AcademicYearId, years.GetValueOrDefault(e.AcademicYearId, "?"), e.TermId, e.TermId is { } t ? terms.GetValueOrDefault(t) : null,
            e.CampusId, e.CampusId is { } c ? campuses.GetValueOrDefault(c) : null, e.Code, e.Name, e.NameBn, e.Type, e.StartDate, e.EndDate, e.SchedulePublished,
            e.ResultStatus, e.PublishAtUtc, e.PublishedAtUtc,
            classRows.Where(x => x.ExamId == e.Id).OrderBy(x => classes.GetValueOrDefault(x.YearGroupId))
                .Select(x => new ExamClassView(x.YearGroupId, classes.GetValueOrDefault(x.YearGroupId, "?"), x.GradingSchemeId,
                    x.GradingSchemeId is { } g ? schemes.GetValueOrDefault(g) : null)).ToList(),
            papers.GetValueOrDefault(e.Id), sheets.Count(s => s.ExamId == e.Id), sheets.Count(s => s.ExamId == e.Id && s.Status == MarkSheetStatus.Approved))).ToList();
    }

    private async Task ValidateAsync(ExamRequest r, Guid? id, CancellationToken ct)
    {
        var year = await db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == r.AcademicYearId, ct) ?? throw new NotFoundException("Academic year", r.AcademicYearId);
        if (r.EndDate < r.StartDate) throw new DomainException("The exam must end on or after its first day.", "bad_range");
        if (r.StartDate < year.StartDate || r.EndDate > year.EndDate) throw new DomainException($"The exam must fall inside {year.Name}.", "outside_year");
        if (r.TermId is { } termId && !await db.Terms.AnyAsync(t => t.Id == termId && t.AcademicYearId == r.AcademicYearId, ct))
            throw new DomainException("The term is not part of this academic year.", "wrong_term");
        if (r.Classes.Count == 0) throw new DomainException("Choose at least one class.", "no_classes");
        if (r.Classes.GroupBy(c => c.YearGroupId).Any(g => g.Count() > 1)) throw new DomainException("A class is listed twice.", "duplicate_class");
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Exams.AnyAsync(e => e.AcademicYearId == r.AcademicYearId && e.Code == code && e.Id != id, ct))
            throw new DomainException($"Exam code {code} is already used this year.", "duplicate_code", isConflict: true);
    }

    private static void Apply(Exam exam, ExamRequest r)
    {
        exam.TermId = r.TermId;
        exam.CampusId = r.CampusId;
        exam.Name = r.Name.Trim();
        exam.NameBn = LearningText.Clean(r.NameBn);
        exam.Type = r.Type;
        exam.StartDate = r.StartDate;
        exam.EndDate = r.EndDate;
    }

    private void ValidatePaper(Exam exam, PaperRequest r)
    {
        if (r.Date < exam.StartDate || r.Date > exam.EndDate) throw new DomainException($"The paper must be between {exam.StartDate:dd MMM} and {exam.EndDate:dd MMM yyyy}.", "outside_exam");
        if (r.EndTime <= r.StartTime) throw new DomainException("The paper must end after it starts.", "bad_time");
        if (r.Components.Count == 0) throw new DomainException("A paper needs at least one marked component (e.g. Written).", "no_components");
        if (r.Components.Sum(c => c.WeightPercent) != 100) throw new DomainException("Component weights must add up to 100%.", "weights_not_100");
        if (r.Components.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new DomainException("Component names must differ.", "duplicate_component");
        if (r.Components.Any(c => c.FromGradebook) && exam.TermId is null) throw new DomainException("Link the exam to a term to take a component from the gradebook.", "no_term");
        if (!db.ExamClasses.Any(c => c.ExamId == exam.Id && c.YearGroupId == r.YearGroupId))
            throw new DomainException("The class is not part of this exam.", "class_not_in_exam");
    }

    private static void EnsureScheduleEditable(Exam exam)
    {
        if (exam.ResultStatus != ResultStatus.NotReady) throw new DomainException("Results are finalised; the date sheet can no longer change.", "results_finalised", isConflict: true);
    }

    /// <summary>A whole-school exam (no campus) needs organisation-wide access.</summary>
    private void EnsureManage(Guid? campusId)
    {
        var ok = campusId is { } c
            ? currentUser.HasPermission(Permissions.Exams.Manage, c)
            : currentUser.HasPermission(Permissions.Exams.Manage) && (currentUser.IsSystem || currentUser.HasAllCampusAccess);
        if (!ok) throw new ForbiddenException(campusId is null ? "Exams for every campus need organisation-wide exam rights." : "You cannot manage exams for this campus.");
    }

    private void EnsureCampus(Guid campusId)
    {
        if (!currentUser.CanAccessCampus(campusId)) throw new ForbiddenException("You do not have access to this campus.");
    }
}
