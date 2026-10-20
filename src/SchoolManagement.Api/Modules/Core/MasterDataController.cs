using SchoolManagement.Domain.Core;

namespace SchoolManagement.Api.Modules.Core;

/// <summary>CORE-07: subjects, houses, holidays and fee heads.</summary>
[ApiController]
[Route("api/master")]
[Tags("Core: Master Data")]
public sealed class MasterDataController(AppDbContext db) : ControllerBase
{
    private async Task<Guid> OrgIdAsync(CancellationToken ct) => await db.Organisations.Select(o => o.Id).FirstAsync(ct);

    // ---- Subjects
    [HttpGet("subjects")]
    [Authorize]
    public async Task<List<SubjectView>> Subjects(CancellationToken ct) =>
        await db.Subjects.AsNoTracking().OrderBy(s => s.Name).Select(s => new SubjectView(s.Id, s.Code, s.Name, s.NameBn, s.Type, s.ExternalCode)).ToListAsync(ct);

    [HttpPost("subjects")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<Guid> CreateSubject(SubjectRequest r, CancellationToken ct)
    {
        var s = new Subject { OrganisationId = await OrgIdAsync(ct), Code = r.Code, Name = r.Name, NameBn = r.NameBn, Type = r.Type, ExternalCode = r.ExternalCode };
        db.Subjects.Add(s);
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    [HttpPut("subjects/{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<IActionResult> UpdateSubject(Guid id, SubjectRequest r, CancellationToken ct)
    {
        var s = await db.Subjects.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Subject", id);
        (s.Code, s.Name, s.NameBn, s.Type, s.ExternalCode) = (r.Code, r.Name, r.NameBn, r.Type, r.ExternalCode);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Houses
    [HttpGet("houses")]
    [Authorize]
    public async Task<List<HouseView>> Houses(CancellationToken ct) =>
        await db.Houses.AsNoTracking().OrderBy(h => h.Name).Select(h => new HouseView(h.Id, h.Name, h.NameBn, h.Colour)).ToListAsync(ct);

    [HttpPost("houses")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<Guid> CreateHouse(HouseRequest r, CancellationToken ct)
    {
        var h = new House { OrganisationId = await OrgIdAsync(ct), Name = r.Name, NameBn = r.NameBn, Colour = r.Colour };
        db.Houses.Add(h);
        await db.SaveChangesAsync(ct);
        return h.Id;
    }

    [HttpPut("houses/{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<IActionResult> UpdateHouse(Guid id, HouseRequest r, CancellationToken ct)
    {
        var h = await db.Houses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("House", id);
        (h.Name, h.NameBn, h.Colour) = (r.Name, r.NameBn, r.Colour);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Holidays
    [HttpGet("holidays")]
    [Authorize]
    public async Task<List<HolidayView>> Holidays([FromQuery] Guid? academicYearId, [FromQuery] Guid? campusId, CancellationToken ct)
    {
        var q = db.Holidays.AsNoTracking();
        q = academicYearId is null
            ? q.Where(h => db.AcademicYears.Any(y => y.Id == h.AcademicYearId && y.IsCurrent))
            : q.Where(h => h.AcademicYearId == academicYearId);
        if (campusId is not null) q = q.Where(h => h.CampusId == null || h.CampusId == campusId);
        return await q.OrderBy(h => h.StartDate)
            .Select(h => new HolidayView(h.Id, h.AcademicYearId, h.CampusId, h.Name, h.NameBn, h.Type, h.StartDate, h.EndDate)).ToListAsync(ct);
    }

    [HttpPost("holidays")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<Guid> CreateHoliday(HolidayRequest r, CancellationToken ct)
    {
        var h = new Holiday { AcademicYearId = r.AcademicYearId, CampusId = r.CampusId, Name = r.Name, NameBn = r.NameBn, Type = r.Type, StartDate = r.StartDate, EndDate = r.EndDate };
        db.Holidays.Add(h);
        await db.SaveChangesAsync(ct);
        return h.Id;
    }

    [HttpDelete("holidays/{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<IActionResult> DeleteHoliday(Guid id, CancellationToken ct)
    {
        var h = await db.Holidays.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Holiday", id);
        db.Holidays.Remove(h);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Fee heads
    [HttpGet("fee-heads")]
    [Authorize]
    public async Task<List<FeeHeadView>> FeeHeads(CancellationToken ct) =>
        await db.FeeHeads.AsNoTracking().OrderBy(f => f.Name).Select(f => new FeeHeadView(f.Id, f.Code, f.Name, f.NameBn, f.Frequency, f.IsRefundable)).ToListAsync(ct);

    [HttpPost("fee-heads")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<Guid> CreateFeeHead(FeeHeadRequest r, CancellationToken ct)
    {
        var f = new FeeHead { OrganisationId = await OrgIdAsync(ct), Code = r.Code.ToUpperInvariant(), Name = r.Name, NameBn = r.NameBn, Frequency = r.Frequency, IsRefundable = r.IsRefundable };
        db.FeeHeads.Add(f);
        await db.SaveChangesAsync(ct);
        return f.Id;
    }

    [HttpPut("fee-heads/{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<IActionResult> UpdateFeeHead(Guid id, FeeHeadRequest r, CancellationToken ct)
    {
        var f = await db.FeeHeads.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Fee head", id);
        (f.Name, f.NameBn, f.Frequency, f.IsRefundable) = (r.Name, r.NameBn, r.Frequency, r.IsRefundable);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record SubjectView(Guid Id, string Code, string Name, string? NameBn, SubjectType Type, string? ExternalCode);
public record SubjectRequest(string Code, string Name, string? NameBn, SubjectType Type, string? ExternalCode);
public record HouseView(Guid Id, string Name, string? NameBn, string? Colour);
public record HouseRequest(string Name, string? NameBn, string? Colour);
public record HolidayView(Guid Id, Guid AcademicYearId, Guid? CampusId, string Name, string? NameBn, HolidayType Type, DateOnly StartDate, DateOnly EndDate);
public record HolidayRequest(Guid AcademicYearId, Guid? CampusId, string Name, string? NameBn, HolidayType Type, DateOnly StartDate, DateOnly EndDate);
public record FeeHeadView(Guid Id, string Code, string Name, string? NameBn, FeeFrequency Frequency, bool IsRefundable);
public record FeeHeadRequest(string Code, string Name, string? NameBn, FeeFrequency Frequency, bool IsRefundable);

public sealed class SubjectRequestValidator : AbstractValidator<SubjectRequest>
{
    public SubjectRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class HolidayRequestValidator : AbstractValidator<HolidayRequest>
{
    public HolidayRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
    }
}

public sealed class FeeHeadRequestValidator : AbstractValidator<FeeHeadRequest>
{
    public FeeHeadRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
