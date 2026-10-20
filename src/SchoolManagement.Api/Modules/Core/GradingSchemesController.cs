using SchoolManagement.Domain.Core;

namespace SchoolManagement.Api.Modules.Core;

/// <summary>CORE-07 / Appendix A: configurable percentage → grade mappings.</summary>
[ApiController]
[Route("api/grading-schemes")]
[Tags("Core: Master Data")]
public sealed class GradingSchemesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<List<GradingSchemeView>> List(CancellationToken ct) =>
        (await db.GradingSchemes.AsNoTracking().Include(s => s.Bands).OrderBy(s => s.Name).ToListAsync(ct)).Select(ToView).ToList();

    [HttpPost]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<GradingSchemeView> Create(GradingSchemeRequest request, CancellationToken ct)
    {
        var org = await db.Organisations.Select(o => o.Id).FirstAsync(ct);
        var scheme = new GradingScheme { OrganisationId = org, Code = request.Code, Name = request.Name, ScaleType = request.ScaleType };
        SetBands(scheme, request.Bands);
        db.GradingSchemes.Add(scheme);
        await db.SaveChangesAsync(ct);
        return ToView(scheme);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<GradingSchemeView> Update(Guid id, GradingSchemeRequest request, CancellationToken ct)
    {
        var scheme = await db.GradingSchemes.Include(s => s.Bands).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Grading scheme", id);
        scheme.Name = request.Name;
        scheme.ScaleType = request.ScaleType;
        db.GradeBands.RemoveRange(scheme.Bands);
        scheme.Bands.Clear();
        SetBands(scheme, request.Bands);
        await db.SaveChangesAsync(ct);
        return ToView(scheme);
    }

    /// <summary>Looks up the grade for a percentage — handy for clients and for checking a new scheme.</summary>
    [HttpGet("{id:guid}/grade")]
    [Authorize]
    public async Task<GradeBandView> Grade(Guid id, [FromQuery] decimal percent, CancellationToken ct)
    {
        var scheme = await db.GradingSchemes.AsNoTracking().Include(s => s.Bands).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Grading scheme", id);
        var band = GradeCalculator.Find(scheme.Bands, percent) ?? throw new DomainException($"No band covers {percent}%.", "no_band");
        return ToBandView(band);
    }

    private static void SetBands(GradingScheme scheme, IEnumerable<GradeBandRequest> bands)
    {
        foreach (var b in bands)
            scheme.Bands.Add(new GradeBand
            {
                GradingSchemeId = scheme.Id, Grade = b.Grade, Descriptor = b.Descriptor, DescriptorBn = b.DescriptorBn,
                MinPercent = b.MinPercent, MaxPercent = b.MaxPercent, GradePoint = b.GradePoint, IsPass = b.IsPass
            });
    }

    private static GradeBandView ToBandView(GradeBand b) => new(b.Grade, b.Descriptor, b.DescriptorBn, b.MinPercent, b.MaxPercent, b.GradePoint, b.IsPass);

    private static GradingSchemeView ToView(GradingScheme s) =>
        new(s.Id, s.Code, s.Name, s.ScaleType, s.Bands.OrderByDescending(b => b.MinPercent).Select(ToBandView).ToList());
}

public static class GradeCalculator
{
    /// <summary>Bands are inclusive on both ends; the highest band containing the value wins.</summary>
    public static GradeBand? Find(IEnumerable<GradeBand> bands, decimal percent) =>
        bands.Where(b => percent >= b.MinPercent && percent <= b.MaxPercent).MaxBy(b => b.MinPercent);

    /// <summary>Checks bands cover 0–100 with no overlaps (gaps of up to 0.01 between 2-dp bands are allowed).</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<GradeBandRequest> bands)
    {
        var errors = new List<string>();
        var ordered = bands.OrderBy(b => b.MinPercent).ToList();
        if (ordered.Count == 0) return ["At least one band is required."];
        if (ordered[0].MinPercent > 0) errors.Add("Bands must start at 0%.");
        if (ordered[^1].MaxPercent < 100) errors.Add("Bands must reach 100%.");
        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];
            if (cur.MinPercent <= prev.MaxPercent) errors.Add($"Bands {prev.Grade} and {cur.Grade} overlap.");
            else if (cur.MinPercent - prev.MaxPercent > 0.01m) errors.Add($"Gap between {prev.Grade} and {cur.Grade}.");
        }
        return errors;
    }
}

public record GradeBandView(string Grade, string? Descriptor, string? DescriptorBn, decimal MinPercent, decimal MaxPercent, decimal? GradePoint, bool IsPass);
public record GradingSchemeView(Guid Id, string Code, string Name, GradingScaleType ScaleType, IReadOnlyList<GradeBandView> Bands);
public record GradeBandRequest(string Grade, string? Descriptor, string? DescriptorBn, decimal MinPercent, decimal MaxPercent, decimal? GradePoint, bool IsPass = true);
public record GradingSchemeRequest(string Code, string Name, GradingScaleType ScaleType, List<GradeBandRequest> Bands);

public sealed class GradingSchemeRequestValidator : AbstractValidator<GradingSchemeRequest>
{
    public GradingSchemeRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleForEach(x => x.Bands).ChildRules(b =>
        {
            b.RuleFor(x => x.Grade).NotEmpty().MaximumLength(16);
            b.RuleFor(x => x.MinPercent).InclusiveBetween(0, 100);
            b.RuleFor(x => x.MaxPercent).InclusiveBetween(0, 100).GreaterThanOrEqualTo(x => x.MinPercent);
        });
        RuleFor(x => x.Bands).Custom((bands, ctx) =>
        {
            foreach (var error in GradeCalculator.Validate(bands ?? [])) ctx.AddFailure(nameof(GradingSchemeRequest.Bands), error);
        });
    }
}
