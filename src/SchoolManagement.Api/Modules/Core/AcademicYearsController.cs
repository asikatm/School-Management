using SchoolManagement.Api.Modules.Students.Services;
using SchoolManagement.Domain.Core;

namespace SchoolManagement.Api.Modules.Core;

/// <summary>CORE-02: academic years and terms — open, lock, archive and the guided roll-over wizard.</summary>
[ApiController]
[Route("api/academic-years")]
[Tags("Core: Campuses & Academic Years")]
public sealed class AcademicYearsController(AppDbContext db, IRolloverService rollover) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<List<AcademicYearView>> List(CancellationToken ct) =>
        await db.AcademicYears.AsNoTracking().Include(y => y.Terms).OrderByDescending(y => y.StartDate)
            .Select(y => ToView(y)).ToListAsync(ct);

    [HttpGet("current")]
    [Authorize]
    public async Task<AcademicYearView> Current(CancellationToken ct)
    {
        var y = await db.AcademicYears.AsNoTracking().Include(x => x.Terms).FirstOrDefaultAsync(x => x.IsCurrent, ct)
                ?? throw new NotFoundException("Academic year", "current");
        return ToView(y);
    }

    [HttpPost]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<AcademicYearView> Create(AcademicYearRequest request, CancellationToken ct)
    {
        var org = await db.Organisations.FirstAsync(ct);
        var year = new AcademicYear { OrganisationId = org.Id, Name = request.Name, StartDate = request.StartDate, EndDate = request.EndDate };
        foreach (var t in request.Terms ?? [])
            year.Terms.Add(new Term { Name = t.Name, NameBn = t.NameBn, Sequence = t.Sequence, StartDate = t.StartDate, EndDate = t.EndDate });
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync(ct);
        return ToView(year);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<AcademicYearView> Update(Guid id, AcademicYearRequest request, CancellationToken ct)
    {
        var year = await Load(id, ct);
        if (!year.IsWritable) throw new DomainException("A locked or archived year cannot be edited.", "year_locked", isConflict: true);
        year.Name = request.Name;
        year.StartDate = request.StartDate;
        year.EndDate = request.EndDate;
        if (request.Terms is not null)
        {
            db.Terms.RemoveRange(year.Terms);
            year.Terms.Clear();
            foreach (var t in request.Terms)
                year.Terms.Add(new Term { AcademicYearId = year.Id, Name = t.Name, NameBn = t.NameBn, Sequence = t.Sequence, StartDate = t.StartDate, EndDate = t.EndDate });
        }
        await db.SaveChangesAsync(ct);
        return ToView(year);
    }

    [HttpPost("{id:guid}/open")]
    [HasPermission(Permissions.Settings.Manage)]
    public Task<AcademicYearView> Open(Guid id, CancellationToken ct) => Transition(id, AcademicYearStatus.Open, ct);

    /// <summary>Makes every record of the year read-only (marks, attendance, enrolments, …).</summary>
    [HttpPost("{id:guid}/lock")]
    [HasPermission(Permissions.Settings.Manage)]
    public Task<AcademicYearView> Lock(Guid id, CancellationToken ct) => Transition(id, AcademicYearStatus.Locked, ct);

    /// <summary>CORE-13: archives the year read-only; its records stay searchable.</summary>
    [HttpPost("{id:guid}/archive")]
    [HasPermission(Permissions.Settings.Manage)]
    public Task<AcademicYearView> Archive(Guid id, CancellationToken ct) => Transition(id, AcademicYearStatus.Archived, ct);

    [HttpPost("{id:guid}/set-current")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<AcademicYearView> SetCurrent(Guid id, CancellationToken ct)
    {
        var year = await Load(id, ct);
        if (year.Status != AcademicYearStatus.Open) throw new DomainException("Only an open year can be made current.", "year_not_open");
        await db.AcademicYears.Where(y => y.IsCurrent && y.Id != id).ForEachAsync(y => y.IsCurrent = false, ct);
        year.IsCurrent = true;
        await db.SaveChangesAsync(ct);
        return ToView(year);
    }

    /// <summary>Shows what the roll-over wizard would do: sections to copy and students per outcome.</summary>
    [HttpGet("{id:guid}/rollover/preview")]
    [HasPermission(Permissions.Settings.Manage)]
    public Task<RolloverPreview> PreviewRollover(Guid id, CancellationToken ct) => rollover.PreviewAsync(id, ct);

    /// <summary>
    /// The roll-over wizard: creates the next year with terms and sections, promotes every student still active in
    /// the source year (or graduates final-year students), and optionally locks the source year.
    /// Students already processed through the promotion review screen (SIS-05) keep their decision.
    /// </summary>
    [HttpPost("{id:guid}/rollover")]
    [HasPermission(Permissions.Settings.Manage)]
    public Task<RolloverResult> Rollover(Guid id, RolloverRequest request, CancellationToken ct) => rollover.RunAsync(id, request, ct);

    private async Task<AcademicYearView> Transition(Guid id, AcademicYearStatus target, CancellationToken ct)
    {
        var year = await Load(id, ct);
        var allowed = (year.Status, target) switch
        {
            (AcademicYearStatus.Draft, AcademicYearStatus.Open) => true,
            (AcademicYearStatus.Locked, AcademicYearStatus.Open) => true,
            (AcademicYearStatus.Open, AcademicYearStatus.Locked) => true,
            (AcademicYearStatus.Locked, AcademicYearStatus.Archived) => true,
            _ => false
        };
        if (!allowed) throw new DomainException($"Cannot move a {year.Status} year to {target}.", "invalid_transition", isConflict: true);
        if (target != AcademicYearStatus.Open && year.IsCurrent)
            throw new DomainException("Make another year current before locking this one.", "year_is_current", isConflict: true);
        year.Status = target;
        await db.SaveChangesAsync(ct);
        return ToView(year);
    }

    private async Task<AcademicYear> Load(Guid id, CancellationToken ct) =>
        await db.AcademicYears.Include(y => y.Terms).FirstOrDefaultAsync(y => y.Id == id, ct) ?? throw new NotFoundException("Academic year", id);

    private static AcademicYearView ToView(AcademicYear y) => new(y.Id, y.Name, y.StartDate, y.EndDate, y.Status, y.IsCurrent,
        y.Terms.OrderBy(t => t.Sequence).Select(t => new TermView(t.Id, t.Name, t.NameBn, t.Sequence, t.StartDate, t.EndDate, t.IsLocked)).ToList());
}

public record TermView(Guid Id, string Name, string? NameBn, int Sequence, DateOnly StartDate, DateOnly EndDate, bool IsLocked);
public record AcademicYearView(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, AcademicYearStatus Status, bool IsCurrent, IReadOnlyList<TermView> Terms);
public record TermRequest(string Name, string? NameBn, int Sequence, DateOnly StartDate, DateOnly EndDate);
public record AcademicYearRequest(string Name, DateOnly StartDate, DateOnly EndDate, List<TermRequest>? Terms);

public sealed class AcademicYearRequestValidator : AbstractValidator<AcademicYearRequest>
{
    public AcademicYearRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate);
        RuleForEach(x => x.Terms).ChildRules(t =>
        {
            t.RuleFor(x => x.Name).NotEmpty();
            t.RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate);
        });
        RuleFor(x => x).Must(x => x.Terms is null || x.Terms.All(t => t.StartDate >= x.StartDate && t.EndDate <= x.EndDate))
            .WithMessage("Terms must fall within the academic year.");
    }
}

public sealed class RolloverRequestValidator : AbstractValidator<RolloverRequest>
{
    public RolloverRequestValidator()
    {
        RuleFor(x => x.NewYearName).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EndDate).GreaterThan(x => x.StartDate);
    }
}
