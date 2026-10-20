using SchoolManagement.Domain.Core;

namespace SchoolManagement.Api.Modules.Core;

/// <summary>CORE-01: campuses of the organisation.</summary>
[ApiController]
[Route("api/campuses")]
[Tags("Core: Campuses & Academic Years")]
public sealed class CampusesController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Campuses the caller can access.</summary>
    [HttpGet]
    [Authorize]
    public async Task<List<CampusView>> List(CancellationToken ct)
    {
        var q = db.Campuses.AsNoTracking();
        if (!currentUser.HasAllCampusAccess)
        {
            var ids = currentUser.CampusIds.ToList();
            q = q.Where(c => ids.Contains(c.Id));
        }
        return await q.OrderBy(c => c.Code).Select(c => ToView(c)).ToListAsync(ct);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<CampusView> Get(Guid id, CancellationToken ct)
    {
        if (!currentUser.CanAccessCampus(id)) throw new ForbiddenException();
        var c = await db.Campuses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Campus", id);
        return ToView(c);
    }

    [HttpPost]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<CampusView> Create(CampusRequest request, CancellationToken ct)
    {
        var org = await db.Organisations.FirstAsync(ct);
        if (await db.Campuses.IgnoreQueryFilters().AnyAsync(c => c.Code == request.Code, ct))
            throw new DomainException($"Campus code '{request.Code}' already exists.", "duplicate", isConflict: true);
        var campus = new Campus { OrganisationId = org.Id, Code = request.Code.ToUpperInvariant(), Name = request.Name };
        Apply(campus, request);
        db.Campuses.Add(campus);
        await db.SaveChangesAsync(ct);
        return ToView(campus);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<CampusView> Update(Guid id, CampusRequest request, CancellationToken ct)
    {
        var campus = await db.Campuses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Campus", id);
        Apply(campus, request);
        await db.SaveChangesAsync(ct);
        return ToView(campus);
    }

    private static void Apply(Campus c, CampusRequest r)
    {
        c.Name = r.Name;
        c.NameBn = r.NameBn;
        c.Address = r.Address;
        c.Phone = r.Phone;
        c.Email = r.Email;
        c.HeadName = r.HeadName;
        c.IsActive = r.IsActive;
    }

    private static CampusView ToView(Campus c) => new(c.Id, c.Code, c.Name, c.NameBn, c.Address, c.Phone, c.Email, c.HeadName, c.IsActive);
}

public record CampusView(Guid Id, string Code, string Name, string? NameBn, string? Address, string? Phone, string? Email, string? HeadName, bool IsActive);
public record CampusRequest(string Code, string Name, string? NameBn, string? Address, string? Phone, string? Email, string? HeadName, bool IsActive = true);

public sealed class CampusRequestValidator : AbstractValidator<CampusRequest>
{
    public CampusRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(8).Matches("^[A-Za-z0-9]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
    }
}
