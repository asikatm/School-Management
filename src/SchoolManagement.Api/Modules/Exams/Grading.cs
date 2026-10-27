using SchoolManagement.Domain.Core;

namespace SchoolManagement.Api.Modules.Exams;

public record GradeBandInfo(string Grade, string? Descriptor, string? DescriptorBn, decimal MinPercent, decimal MaxPercent, decimal? GradePoint, bool IsPass);

/// <summary>A grading scheme ready for lookups (EXM-04: 9–1, A*–E, GPA 5.00, primary descriptors, letter grades).</summary>
public record GradeScale(Guid Id, string Code, string Name, GradingScaleType ScaleType, IReadOnlyList<GradeBandInfo> Bands)
{
    /// <summary>The band whose range holds the percentage; percentages falling in a gap take the band below.</summary>
    public GradeBandInfo? BandFor(decimal percent)
    {
        var p = Math.Round(percent, 2);
        return Bands.Where(b => b.MinPercent <= p).OrderByDescending(b => b.MinPercent).FirstOrDefault()
               ?? Bands.OrderBy(b => b.MinPercent).FirstOrDefault();
    }

    public bool UsesGpa => ScaleType == GradingScaleType.Gpa5 || Bands.Any(b => b.GradePoint is not null);
}

/// <summary>Loads grading schemes and resolves which one a class uses.</summary>
public sealed class Grading(AppDbContext db)
{
    private Dictionary<Guid, GradeScale>? schemes;

    public async Task<Dictionary<Guid, GradeScale>> SchemesAsync(CancellationToken ct)
    {
        if (schemes is not null) return schemes;
        var rows = await db.GradingSchemes.IgnoreQueryFilters().AsNoTracking().Include(s => s.Bands).Where(s => !s.IsDeleted).ToListAsync(ct);
        return schemes = rows.ToDictionary(s => s.Id, s => new GradeScale(s.Id, s.Code, s.Name, s.ScaleType,
            s.Bands.OrderByDescending(b => b.MinPercent)
                .Select(b => new GradeBandInfo(b.Grade, b.Descriptor, b.DescriptorBn, b.MinPercent, b.MaxPercent, b.GradePoint, b.IsPass)).ToList()));
    }

    /// <summary>The class's programme default scheme, unless <paramref name="overrideId"/> is given.</summary>
    public async Task<GradeScale?> ForYearGroupAsync(Guid yearGroupId, Guid? overrideId, CancellationToken ct)
    {
        var all = await SchemesAsync(ct);
        if (overrideId is { } id && all.TryGetValue(id, out var chosen)) return chosen;
        var programmeScheme = await db.YearGroups.IgnoreQueryFilters().AsNoTracking().Where(y => y.Id == yearGroupId)
            .Select(y => y.Programme!.DefaultGradingSchemeId).FirstOrDefaultAsync(ct);
        return programmeScheme is { } p ? all.GetValueOrDefault(p) : null;
    }

    /// <summary>
    /// Overall GPA: the mean grade point of the subjects (2 decimals). With <paramref name="zeroOnFail"/> (the local GPA 5.00
    /// rule) any failed subject makes the GPA 0.00. Null when the scale has no grade points.
    /// </summary>
    public static decimal? Gpa(IReadOnlyCollection<(decimal? GradePoint, bool Passed)> subjects, bool zeroOnFail)
    {
        var points = subjects.Where(s => s.GradePoint is not null).ToList();
        if (points.Count == 0) return null;
        if (zeroOnFail && subjects.Any(s => !s.Passed)) return 0m;
        return Math.Round(points.Average(s => s.GradePoint!.Value), 2);
    }
}
