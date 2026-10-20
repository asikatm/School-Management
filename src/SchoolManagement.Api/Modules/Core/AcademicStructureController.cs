using SchoolManagement.Domain.Core;

namespace SchoolManagement.Api.Modules.Core;

/// <summary>CORE-07 / ACA-01 groundwork: programmes, year-groups (classes), sections and shifts.</summary>
[ApiController]
[Route("api/structure")]
[Tags("Core: Master Data")]
public sealed class AcademicStructureController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("programmes")]
    [Authorize]
    public async Task<List<ProgrammeView>> Programmes(CancellationToken ct) =>
        await db.Programmes.AsNoTracking().OrderBy(p => p.Sequence)
            .Select(p => new ProgrammeView(p.Id, p.Code, p.Name, p.NameBn, p.Sequence, p.DefaultGradingSchemeId,
                p.YearGroups.Where(y => !y.IsDeleted).OrderBy(y => y.Sequence)
                    .Select(y => new YearGroupView(y.Id, y.ProgrammeId, y.Code, y.Name, y.NameBn, y.Sequence, y.NextYearGroupId, y.IsFinalYear, y.MinAgeMonths, y.MaxAgeMonths, y.AgeCutOffMonth, y.AgeCutOffDay)).ToList()))
            .ToListAsync(ct);

    [HttpPost("programmes")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<Guid> CreateProgramme(ProgrammeRequest request, CancellationToken ct)
    {
        var org = await db.Organisations.FirstAsync(ct);
        var p = new Programme { OrganisationId = org.Id, Code = request.Code, Name = request.Name, NameBn = request.NameBn, Sequence = request.Sequence, DefaultGradingSchemeId = request.DefaultGradingSchemeId };
        db.Programmes.Add(p);
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    [HttpPut("programmes/{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<IActionResult> UpdateProgramme(Guid id, ProgrammeRequest request, CancellationToken ct)
    {
        var p = await db.Programmes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Programme", id);
        p.Code = request.Code;
        p.Name = request.Name;
        p.NameBn = request.NameBn;
        p.Sequence = request.Sequence;
        p.DefaultGradingSchemeId = request.DefaultGradingSchemeId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("year-groups")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<Guid> CreateYearGroup(YearGroupRequest request, CancellationToken ct)
    {
        var y = new YearGroup { ProgrammeId = request.ProgrammeId, Code = request.Code, Name = request.Name };
        Apply(y, request);
        db.YearGroups.Add(y);
        await db.SaveChangesAsync(ct);
        return y.Id;
    }

    [HttpPut("year-groups/{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<IActionResult> UpdateYearGroup(Guid id, YearGroupRequest request, CancellationToken ct)
    {
        var y = await db.YearGroups.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Year group", id);
        y.ProgrammeId = request.ProgrammeId;
        y.Code = request.Code;
        y.Name = request.Name;
        Apply(y, request);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(YearGroup y, YearGroupRequest r)
    {
        y.NameBn = r.NameBn;
        y.Sequence = r.Sequence;
        y.NextYearGroupId = r.NextYearGroupId;
        y.IsFinalYear = r.IsFinalYear;
        y.MinAgeMonths = r.MinAgeMonths;
        y.MaxAgeMonths = r.MaxAgeMonths;
        y.AgeCutOffMonth = r.AgeCutOffMonth;
        y.AgeCutOffDay = r.AgeCutOffDay;
    }

    [HttpGet("sections")]
    [Authorize]
    public async Task<List<SectionView>> Sections([FromQuery] Guid? campusId, [FromQuery] Guid? academicYearId, [FromQuery] Guid? yearGroupId, CancellationToken ct)
    {
        var q = db.Sections.AsNoTracking();
        if (campusId is not null) q = q.Where(s => s.CampusId == campusId);
        if (academicYearId is not null) q = q.Where(s => s.AcademicYearId == academicYearId);
        else q = q.Where(s => db.AcademicYears.Any(y => y.Id == s.AcademicYearId && y.IsCurrent));
        if (yearGroupId is not null) q = q.Where(s => s.YearGroupId == yearGroupId);

        return await q.OrderBy(s => s.YearGroup!.Sequence).ThenBy(s => s.Name)
            .Select(s => new SectionView(s.Id, s.CampusId, s.AcademicYearId, s.YearGroupId, s.YearGroup!.Name, s.Name, s.Capacity, s.ShiftId, s.ClassTeacherUserId, s.Room,
                db.StudentEnrolments.Count(e => e.SectionId == s.Id && e.Status == Domain.Students.EnrolmentStatus.Active)))
            .ToListAsync(ct);
    }

    [HttpPost("sections")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<Guid> CreateSection(SectionRequest request, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Settings.Manage, request.CampusId);
        var s = new Section { CampusId = request.CampusId, AcademicYearId = request.AcademicYearId, YearGroupId = request.YearGroupId, Name = request.Name };
        Apply(s, request);
        db.Sections.Add(s);
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    [HttpPut("sections/{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<IActionResult> UpdateSection(Guid id, SectionRequest request, CancellationToken ct)
    {
        var s = await db.Sections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Section", id);
        currentUser.EnsurePermission(Permissions.Settings.Manage, s.CampusId);
        s.Name = request.Name;
        Apply(s, request);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("sections/{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<IActionResult> DeleteSection(Guid id, CancellationToken ct)
    {
        var s = await db.Sections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Section", id);
        currentUser.EnsurePermission(Permissions.Settings.Manage, s.CampusId);
        if (await db.StudentEnrolments.AnyAsync(e => e.SectionId == id && e.Status == Domain.Students.EnrolmentStatus.Active, ct))
            throw new DomainException("The section still has students.", "section_in_use", isConflict: true);
        db.Sections.Remove(s);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(Section s, SectionRequest r)
    {
        s.Capacity = r.Capacity;
        s.ShiftId = r.ShiftId;
        s.ClassTeacherUserId = r.ClassTeacherUserId;
        s.Room = r.Room;
    }

    [HttpGet("shifts")]
    [Authorize]
    public async Task<List<ShiftView>> Shifts([FromQuery] Guid? campusId, CancellationToken ct) =>
        await db.Shifts.AsNoTracking().Where(s => campusId == null || s.CampusId == campusId)
            .OrderBy(s => s.StartTime).Select(s => new ShiftView(s.Id, s.CampusId, s.Name, s.StartTime, s.EndTime)).ToListAsync(ct);

    [HttpPost("shifts")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<Guid> CreateShift(ShiftRequest request, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Settings.Manage, request.CampusId);
        var s = new Shift { CampusId = request.CampusId, Name = request.Name, StartTime = request.StartTime, EndTime = request.EndTime };
        db.Shifts.Add(s);
        await db.SaveChangesAsync(ct);
        return s.Id;
    }
}

public record YearGroupView(Guid Id, Guid ProgrammeId, string Code, string Name, string? NameBn, int Sequence, Guid? NextYearGroupId, bool IsFinalYear, int? MinAgeMonths, int? MaxAgeMonths, int AgeCutOffMonth, int AgeCutOffDay);
public record ProgrammeView(Guid Id, string Code, string Name, string? NameBn, int Sequence, Guid? DefaultGradingSchemeId, IReadOnlyList<YearGroupView> YearGroups);
public record ProgrammeRequest(string Code, string Name, string? NameBn, int Sequence, Guid? DefaultGradingSchemeId);
public record YearGroupRequest(Guid ProgrammeId, string Code, string Name, string? NameBn, int Sequence, Guid? NextYearGroupId, bool IsFinalYear, int? MinAgeMonths, int? MaxAgeMonths, int AgeCutOffMonth = 7, int AgeCutOffDay = 1);
public record SectionView(Guid Id, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string YearGroupName, string Name, int Capacity, Guid? ShiftId, Guid? ClassTeacherUserId, string? Room, int StudentCount);
public record SectionRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string Name, int Capacity, Guid? ShiftId, Guid? ClassTeacherUserId, string? Room);
public record ShiftView(Guid Id, Guid CampusId, string Name, TimeOnly StartTime, TimeOnly EndTime);
public record ShiftRequest(Guid CampusId, string Name, TimeOnly StartTime, TimeOnly EndTime);

public sealed class YearGroupRequestValidator : AbstractValidator<YearGroupRequest>
{
    public YearGroupRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AgeCutOffMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.AgeCutOffDay).InclusiveBetween(1, 31);
        RuleFor(x => x.MaxAgeMonths).GreaterThanOrEqualTo(x => x.MinAgeMonths).When(x => x.MinAgeMonths is not null && x.MaxAgeMonths is not null);
    }
}

public sealed class SectionRequestValidator : AbstractValidator<SectionRequest>
{
    public SectionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 200);
    }
}
