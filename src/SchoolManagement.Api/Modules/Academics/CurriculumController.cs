using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Core;
using SchoolManagement.Domain.Students;

namespace SchoolManagement.Api.Modules.Academics;

public record RoomView(Guid Id, Guid CampusId, string Code, string Name, RoomType Type, int Capacity, bool IsActive, string? Notes);
public record RoomRequest(Guid CampusId, string Code, string Name, RoomType Type, int Capacity, bool IsActive, string? Notes);

public record PeriodRequest(int PeriodNo, string Name, TimeOnly StartTime, TimeOnly EndTime, bool IsBreak);
public record BellScheduleRequest(Guid CampusId, Guid? ShiftId, List<PeriodRequest> Periods);

public record SubjectGroupView(Guid Id, string Name, int MinChoices, int MaxChoices);
public record CurriculumRow(Guid SubjectId, string Code, string Name, SubjectType Type, string? GroupName, int PeriodsPerWeek, RoomType? PreferredRoomType);
public record CurriculumView(Guid YearGroupId, IReadOnlyList<SubjectGroupView> Groups, IReadOnlyList<CurriculumRow> Subjects, int TotalPeriods);
public record GroupRequest(string Name, int MinChoices, int MaxChoices);
public record CurriculumRowRequest(Guid SubjectId, SubjectType Type, string? GroupName, int PeriodsPerWeek, RoomType? PreferredRoomType);
public record CurriculumRequest(List<GroupRequest> Groups, List<CurriculumRowRequest> Subjects);

public record StudentChoiceRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, IReadOnlyList<Guid> SubjectIds, IReadOnlyList<string> Problems);
public record SubjectChoicesView(Guid SectionId, string SectionLabel, Guid AcademicYearId, IReadOnlyList<SubjectGroupView> Groups,
    IReadOnlyList<CurriculumRow> Choosable, IReadOnlyList<StudentChoiceRow> Students);
public record SubjectChoiceRequest(Guid AcademicYearId, List<Guid> SubjectIds);

public record TeacherOption(Guid Id, string FullName, string UserName);

/// <summary>ACA-01/02 curriculum and subject combinations; ACA-04 groundwork: rooms and bell schedules.</summary>
[ApiController]
[Route("api/academics")]
[Tags("Academics: Curriculum & set-up")]
public sealed class CurriculumController(AppDbContext db, AcademicLookups lookups, ICurrentUser currentUser) : ControllerBase
{
    // ---------------------------------------------------------------- rooms

    [HttpGet("rooms")]
    [HasPermission(Permissions.Academic.View)]
    public async Task<List<RoomView>> Rooms([FromQuery] Guid? campusId, CancellationToken ct) =>
        await db.Rooms.AsNoTracking().Where(r => campusId == null || r.CampusId == campusId).OrderBy(r => r.Code)
            .Select(r => new RoomView(r.Id, r.CampusId, r.Code, r.Name, r.Type, r.Capacity, r.IsActive, r.Notes)).ToListAsync(ct);

    [HttpPost("rooms")]
    [HasPermission(Permissions.Academic.Manage)]
    public async Task<RoomView> CreateRoom(RoomRequest r, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Academic.Manage, r.CampusId);
        if (await db.Rooms.IgnoreQueryFilters().AnyAsync(x => x.CampusId == r.CampusId && x.Code == r.Code.Trim(), ct))
            throw new DomainException($"Room code '{r.Code}' already exists on this campus.", "duplicate", isConflict: true);
        var room = new Room { CampusId = r.CampusId, Code = r.Code.Trim(), Name = r.Name.Trim() };
        Apply(room, r);
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        return new RoomView(room.Id, room.CampusId, room.Code, room.Name, room.Type, room.Capacity, room.IsActive, room.Notes);
    }

    [HttpPut("rooms/{id:guid}")]
    [HasPermission(Permissions.Academic.Manage)]
    public async Task<RoomView> UpdateRoom(Guid id, RoomRequest r, CancellationToken ct)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Room", id);
        currentUser.EnsurePermission(Permissions.Academic.Manage, room.CampusId);
        room.Code = r.Code.Trim();
        room.Name = r.Name.Trim();
        Apply(room, r);
        await db.SaveChangesAsync(ct);
        return new RoomView(room.Id, room.CampusId, room.Code, room.Name, room.Type, room.Capacity, room.IsActive, room.Notes);
    }

    private static void Apply(Room room, RoomRequest r)
    {
        room.Type = r.Type;
        room.Capacity = r.Capacity;
        room.IsActive = r.IsActive;
        room.Notes = r.Notes;
    }

    // ---------------------------------------------------------------- bell schedule

    [HttpGet("periods")]
    [Authorize]
    public Task<IReadOnlyList<PeriodView>> Periods([FromQuery] Guid campusId, [FromQuery] Guid? shiftId, CancellationToken ct) =>
        lookups.PeriodsAsync(campusId, shiftId, ct);

    [HttpGet("working-days")]
    [Authorize]
    public Task<IReadOnlyList<DayOfWeek>> WorkingDays([FromQuery] Guid? campusId, CancellationToken ct) => lookups.WorkingDaysAsync(campusId, ct);

    /// <summary>Replaces a campus's (or shift's) bell schedule. Periods already used by a timetable cannot be removed or turned into breaks.</summary>
    [HttpPut("periods")]
    [HasPermission(Permissions.Academic.Manage)]
    public async Task<IReadOnlyList<PeriodView>> SavePeriods(BellScheduleRequest r, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Academic.Manage, r.CampusId);
        var sectionIds = await db.Sections.Where(s => s.CampusId == r.CampusId && (r.ShiftId == null || s.ShiftId == r.ShiftId)).Select(s => s.Id).ToListAsync(ct);
        var used = await db.TimetableSlots.Where(s => sectionIds.Contains(s.SectionId)).Select(s => s.PeriodNo).Distinct().ToListAsync(ct);
        var teaching = r.Periods.Where(p => !p.IsBreak).Select(p => p.PeriodNo).ToHashSet();
        var lost = used.Where(u => !teaching.Contains(u)).ToList();
        if (lost.Count > 0)
            throw new DomainException($"Period(s) {string.Join(", ", lost.Order())} are used in timetables; move those lessons first.", "period_in_use", isConflict: true);

        var existing = await db.PeriodDefinitions.Where(p => p.CampusId == r.CampusId && p.ShiftId == r.ShiftId).ToListAsync(ct);
        db.PeriodDefinitions.RemoveRange(existing);
        // Removing then re-adding the same (campus, shift, period) would hit the unique index in one batch; save the delete first.
        await db.SaveChangesAsync(ct);
        foreach (var p in r.Periods.OrderBy(p => p.PeriodNo))
            db.PeriodDefinitions.Add(new PeriodDefinition
            {
                CampusId = r.CampusId, ShiftId = r.ShiftId, PeriodNo = p.PeriodNo, Name = p.Name.Trim(), StartTime = p.StartTime, EndTime = p.EndTime, IsBreak = p.IsBreak
            });
        await db.SaveChangesAsync(ct);
        return await lookups.PeriodsAsync(r.CampusId, r.ShiftId, ct);
    }

    // ---------------------------------------------------------------- curriculum

    [HttpGet("curriculum/{yearGroupId:guid}")]
    [HasPermission(Permissions.Academic.View)]
    public async Task<CurriculumView> Curriculum(Guid yearGroupId, CancellationToken ct)
    {
        var groups = await db.SubjectGroups.AsNoTracking().Where(g => g.YearGroupId == yearGroupId).OrderBy(g => g.Sequence).ThenBy(g => g.Name).ToListAsync(ct);
        var rows = await db.CurriculumSubjects.AsNoTracking().Include(c => c.Subject).Where(c => c.YearGroupId == yearGroupId)
            .OrderBy(c => c.Sequence).ToListAsync(ct);
        var view = rows.Select(c => new CurriculumRow(c.SubjectId, c.Subject!.Code, c.Subject.Name, c.Type,
            c.SubjectGroupId is { } g ? groups.FirstOrDefault(x => x.Id == g)?.Name : null, c.PeriodsPerWeek, c.PreferredRoomType)).ToList();
        // A student takes every compulsory subject plus, per group, at most MaxChoices; the busiest combination is what must fit in the week.
        var total = view.Where(v => v.GroupName is null && v.Type == SubjectType.Compulsory).Sum(v => v.PeriodsPerWeek)
                    + groups.Sum(g => view.Where(v => v.GroupName == g.Name).Select(v => v.PeriodsPerWeek).OrderDescending().Take(g.MaxChoices).Sum())
                    + view.Where(v => v.GroupName is null && v.Type != SubjectType.Compulsory).Sum(v => v.PeriodsPerWeek);
        return new CurriculumView(yearGroupId, groups.Select(g => new SubjectGroupView(g.Id, g.Name, g.MinChoices, g.MaxChoices)).ToList(), view, total);
    }

    [HttpPut("curriculum/{yearGroupId:guid}")]
    [HasPermission(Permissions.Academic.Manage)]
    public async Task<CurriculumView> SaveCurriculum(Guid yearGroupId, CurriculumRequest r, CancellationToken ct)
    {
        if (!await db.YearGroups.AnyAsync(y => y.Id == yearGroupId, ct)) throw new NotFoundException("Class", yearGroupId);
        var unknownGroups = r.Subjects.Where(s => s.GroupName is not null && r.Groups.All(g => g.Name != s.GroupName)).Select(s => s.GroupName).Distinct().ToList();
        if (unknownGroups.Count > 0) throw new DomainException($"Unknown option group(s): {string.Join(", ", unknownGroups)}.", "unknown_group");

        await ReplaceCurriculumAsync(yearGroupId, r, ct);
        await db.SaveChangesAsync(ct);
        return await Curriculum(yearGroupId, ct);
    }

    /// <summary>Copies one class's curriculum (subjects, periods, option groups) to other classes, replacing theirs.</summary>
    [HttpPost("curriculum/{yearGroupId:guid}/copy")]
    [HasPermission(Permissions.Academic.Manage)]
    public async Task<int> CopyCurriculum(Guid yearGroupId, [FromBody] List<Guid> targetYearGroupIds, CancellationToken ct)
    {
        var source = await Curriculum(yearGroupId, ct);
        var request = new CurriculumRequest(
            source.Groups.Select(g => new GroupRequest(g.Name, g.MinChoices, g.MaxChoices)).ToList(),
            source.Subjects.Select(s => new CurriculumRowRequest(s.SubjectId, s.Type, s.GroupName, s.PeriodsPerWeek, s.PreferredRoomType)).ToList());
        var targets = targetYearGroupIds.Where(t => t != yearGroupId).Distinct().ToList();
        foreach (var target in targets) await ReplaceCurriculumAsync(target, request, ct);
        await db.SaveChangesAsync(ct);
        return targets.Count;
    }

    private async Task ReplaceCurriculumAsync(Guid yearGroupId, CurriculumRequest r, CancellationToken ct)
    {
        db.CurriculumSubjects.RemoveRange(await db.CurriculumSubjects.Where(c => c.YearGroupId == yearGroupId).ToListAsync(ct));
        db.SubjectGroups.RemoveRange(await db.SubjectGroups.Where(g => g.YearGroupId == yearGroupId).ToListAsync(ct));
        // The unique (class, subject) index needs the old rows gone before the new ones arrive.
        await db.SaveChangesAsync(ct);

        var groups = new Dictionary<string, Guid>();
        var seq = 0;
        foreach (var g in r.Groups)
        {
            var group = new SubjectGroup { YearGroupId = yearGroupId, Name = g.Name.Trim(), MinChoices = g.MinChoices, MaxChoices = g.MaxChoices, Sequence = seq++ };
            db.SubjectGroups.Add(group);
            groups[g.Name] = group.Id;
        }
        seq = 0;
        foreach (var s in r.Subjects.DistinctBy(s => s.SubjectId))
            db.CurriculumSubjects.Add(new CurriculumSubject
            {
                YearGroupId = yearGroupId, SubjectId = s.SubjectId, PeriodsPerWeek = s.PeriodsPerWeek, PreferredRoomType = s.PreferredRoomType, Sequence = seq++,
                // A subject in an option group is never compulsory.
                Type = s.GroupName is not null && s.Type == SubjectType.Compulsory ? SubjectType.Elective : s.Type,
                SubjectGroupId = s.GroupName is null ? null : groups[s.GroupName]
            });
    }

    // ---------------------------------------------------------------- ACA-02 student subject choices

    [HttpGet("subject-choices")]
    [HasPermission(Permissions.Academic.View)]
    public async Task<SubjectChoicesView> SubjectChoices([FromQuery] Guid sectionId, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        if (!currentUser.CanAccessCampus(section.CampusId)) throw new ForbiddenException();
        var curriculum = await Curriculum(section.YearGroupId, ct);
        var choosable = curriculum.Subjects.Where(s => s.Type != SubjectType.Compulsory).ToList();

        var students = await db.StudentEnrolments.AsNoTracking()
            .Where(e => e.SectionId == sectionId && e.Status == EnrolmentStatus.Active)
            .OrderBy(e => e.RollNo).ThenBy(e => e.Student!.FirstName)
            .Select(e => new { e.StudentId, e.Student!.AdmissionNo, e.Student.FirstName, e.Student.LastName, e.RollNo })
            .ToListAsync(ct);
        var ids = students.Select(s => s.StudentId).ToList();
        var chosen = await db.StudentSubjects.AsNoTracking().Where(s => s.AcademicYearId == section.AcademicYearId && ids.Contains(s.StudentId))
            .GroupBy(s => s.StudentId).ToDictionaryAsync(g => g.Key, g => g.Select(x => x.SubjectId).ToList(), ct);

        var rows = students.Select(s =>
        {
            var picks = chosen.GetValueOrDefault(s.StudentId) ?? [];
            return new StudentChoiceRow(s.StudentId, s.AdmissionNo, $"{s.FirstName} {s.LastName}".Trim(), s.RollNo, picks, Problems(curriculum, picks));
        }).ToList();
        return new SubjectChoicesView(sectionId, section.Label, section.AcademicYearId, curriculum.Groups, choosable, rows);
    }

    /// <summary>Sets a student's optional/elective subjects for a year; option-group minimums and maximums are enforced.</summary>
    [HttpPut("subject-choices/{studentId:guid}")]
    [HasPermission(Permissions.Academic.Manage)]
    public async Task<StudentChoiceRow> SetSubjectChoices(Guid studentId, SubjectChoiceRequest r, CancellationToken ct)
    {
        var enrolment = await db.StudentEnrolments.Include(e => e.Student)
            .Where(e => e.StudentId == studentId && e.AcademicYearId == r.AcademicYearId && e.Status == EnrolmentStatus.Active)
            .FirstOrDefaultAsync(ct) ?? throw new DomainException("The student has no active enrolment in that year.", "no_enrolment");
        currentUser.EnsurePermission(Permissions.Academic.Manage, enrolment.CampusId);

        var curriculum = await Curriculum(enrolment.YearGroupId, ct);
        var allowed = curriculum.Subjects.Where(s => s.Type != SubjectType.Compulsory).Select(s => s.SubjectId).ToHashSet();
        var picks = r.SubjectIds.Distinct().ToList();
        var notAllowed = picks.Where(p => !allowed.Contains(p)).ToList();
        if (notAllowed.Count > 0) throw new DomainException("Some subjects are not optional or elective subjects of this class.", "invalid_subject");
        var problems = Problems(curriculum, picks);
        if (problems.Count > 0) throw new DomainException(string.Join(" ", problems), "invalid_combination");

        db.StudentSubjects.RemoveRange(await db.StudentSubjects.Where(s => s.StudentId == studentId && s.AcademicYearId == r.AcademicYearId).ToListAsync(ct));
        foreach (var p in picks)
            db.StudentSubjects.Add(new StudentSubject { StudentId = studentId, AcademicYearId = r.AcademicYearId, SubjectId = p, CampusId = enrolment.CampusId });
        await db.SaveChangesAsync(ct);
        var s = enrolment.Student!;
        return new StudentChoiceRow(studentId, s.AdmissionNo, s.FullName, enrolment.RollNo, picks, []);
    }

    private static List<string> Problems(CurriculumView curriculum, IReadOnlyCollection<Guid> picks)
    {
        var problems = new List<string>();
        foreach (var g in curriculum.Groups)
        {
            var count = curriculum.Subjects.Count(s => s.GroupName == g.Name && picks.Contains(s.SubjectId));
            if (count < g.MinChoices || count > g.MaxChoices)
                problems.Add(g.MinChoices == g.MaxChoices
                    ? $"{g.Name}: choose {g.MinChoices} (has {count})."
                    : $"{g.Name}: choose {g.MinChoices}–{g.MaxChoices} (has {count}).");
        }
        return problems;
    }

    // ---------------------------------------------------------------- teachers

    /// <summary>Staff who can be given teaching work on a campus: active staff with a teaching or leadership role there.</summary>
    [HttpGet("teachers")]
    [HasPermission(Permissions.Academic.View)]
    public async Task<List<TeacherOption>> Teachers([FromQuery] Guid? campusId, CancellationToken ct)
    {
        var roles = new[] { RoleNames.ClassTeacher, RoleNames.SubjectTeacher, RoleNames.HeadOfSection, RoleNames.VicePrincipal, RoleNames.Principal };
        return await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.UserType == UserType.Staff &&
                        u.RoleAssignments.Any(a => roles.Contains(a.Role!.Name) && (campusId == null || a.CampusId == null || a.CampusId == campusId)))
            .OrderBy(u => u.FullName)
            .Select(u => new TeacherOption(u.Id, u.FullName, u.UserName))
            .ToListAsync(ct);
    }
}

public sealed class RoomRequestValidator : AbstractValidator<RoomRequest>
{
    public RoomRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 2000);
    }
}

public sealed class BellScheduleRequestValidator : AbstractValidator<BellScheduleRequest>
{
    public BellScheduleRequestValidator()
    {
        RuleFor(x => x.Periods).NotEmpty().Must(p => p.Select(x => x.PeriodNo).Distinct().Count() == p.Count).WithMessage("Period numbers must be unique.");
        RuleForEach(x => x.Periods).ChildRules(p =>
        {
            p.RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
            p.RuleFor(x => x.PeriodNo).InclusiveBetween(1, 20);
            p.RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime);
        });
        RuleFor(x => x.Periods).Must(NoOverlap).WithMessage("Periods must not overlap in time.");
    }

    private static bool NoOverlap(List<PeriodRequest> periods)
    {
        var ordered = periods.OrderBy(p => p.StartTime).ToList();
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].StartTime < ordered[i - 1].EndTime) return false;
        return true;
    }
}

public sealed class CurriculumRequestValidator : AbstractValidator<CurriculumRequest>
{
    public CurriculumRequestValidator()
    {
        RuleForEach(x => x.Groups).ChildRules(g =>
        {
            g.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            g.RuleFor(x => x.MinChoices).GreaterThanOrEqualTo(0);
            g.RuleFor(x => x.MaxChoices).GreaterThanOrEqualTo(x => x.MinChoices).GreaterThan(0);
        });
        RuleFor(x => x.Groups).Must(g => g.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == g.Count)
            .WithMessage("Option group names must be unique.");
        RuleForEach(x => x.Subjects).ChildRules(s => s.RuleFor(x => x.PeriodsPerWeek).InclusiveBetween(0, 20));
    }
}
