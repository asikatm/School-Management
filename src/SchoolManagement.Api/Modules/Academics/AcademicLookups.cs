using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Core;
using SchoolManagement.Infrastructure.Localization;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Academics;

public record PeriodView(int PeriodNo, string Name, TimeOnly StartTime, TimeOnly EndTime, bool IsBreak);

/// <summary>A section with everything the timetable needs to place it in time.</summary>
public record SectionInfo(Guid Id, Guid CampusId, Guid AcademicYearId, Guid YearGroupId, Guid? ShiftId, string Label, string? HomeRoom, Guid? ClassTeacherUserId);

/// <summary>Shared look-ups for the ACA module: teaching days, bell schedules, labels and time overlap.</summary>
public sealed class AcademicLookups(AppDbContext db, ISettingsService settings, TimeProvider clock)
{
    private readonly Dictionary<(Guid, Guid?), IReadOnlyList<PeriodView>> periodCache = [];

    public static readonly DayOfWeek[] DefaultDays =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

    public DateOnly TodayDhaka => DateOnly.FromDateTime(BanglaFormat.ToDhaka(clock.GetUtcNow().UtcDateTime));

    public async Task<IReadOnlyList<DayOfWeek>> WorkingDaysAsync(Guid? campusId, CancellationToken ct)
    {
        var raw = await settings.GetAsync(SettingKeys.WorkingDays, campusId, ct);
        if (string.IsNullOrWhiteSpace(raw)) return DefaultDays;
        var days = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => Enum.TryParse<DayOfWeek>(d, true, out var day) ? (DayOfWeek?)day : null)
            .OfType<DayOfWeek>().Distinct().ToList();
        // Keep the school week order starting on Saturday.
        return days.Count == 0 ? DefaultDays : days.OrderBy(d => ((int)d + 1) % 7).ToList();
    }

    /// <summary>The bell schedule for a campus/shift; falls back to the campus-wide schedule when the shift has none.</summary>
    public async Task<IReadOnlyList<PeriodView>> PeriodsAsync(Guid campusId, Guid? shiftId, CancellationToken ct)
    {
        if (periodCache.TryGetValue((campusId, shiftId), out var cached)) return cached;
        var rows = await db.PeriodDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.CampusId == campusId && (p.ShiftId == shiftId || p.ShiftId == null))
            .ToListAsync(ct);
        var chosen = rows.Any(p => p.ShiftId == shiftId && shiftId != null) ? rows.Where(p => p.ShiftId == shiftId) : rows.Where(p => p.ShiftId == null);
        var list = chosen.OrderBy(p => p.StartTime).Select(p => new PeriodView(p.PeriodNo, p.Name, p.StartTime, p.EndTime, p.IsBreak)).ToList();
        periodCache[(campusId, shiftId)] = list;
        return list;
    }

    public async Task<SectionInfo> SectionAsync(Guid sectionId, CancellationToken ct) =>
        (await SectionsAsync([sectionId], ct)).GetValueOrDefault(sectionId) ?? throw new NotFoundException("Section", sectionId);

    /// <summary>Loads sections across campuses (a teacher's other campus must still count for clashes).</summary>
    public async Task<Dictionary<Guid, SectionInfo>> SectionsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        var multiCampus = await db.Campuses.IgnoreQueryFilters().CountAsync(c => !c.IsDeleted, ct) > 1;
        var rows = await db.Sections.IgnoreQueryFilters().AsNoTracking()
            .Where(s => list.Contains(s.Id) && !s.IsDeleted)
            .Select(s => new
            {
                s.Id, s.CampusId, s.AcademicYearId, s.YearGroupId, s.ShiftId, s.Name, s.Room, s.ClassTeacherUserId,
                Class = s.YearGroup!.Name,
                Campus = db.Campuses.IgnoreQueryFilters().Where(c => c.Id == s.CampusId).Select(c => c.Code).FirstOrDefault()
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => new SectionInfo(r.Id, r.CampusId, r.AcademicYearId, r.YearGroupId, r.ShiftId,
            multiCampus ? $"{r.Class} - {r.Name} ({r.Campus})" : $"{r.Class} - {r.Name}", r.Room, r.ClassTeacherUserId));
    }

    public async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    public async Task<Dictionary<Guid, (string Code, string Name)>> SubjectsAsync(CancellationToken ct) =>
        (await db.Subjects.IgnoreQueryFilters().AsNoTracking().Select(s => new { s.Id, s.Code, s.Name }).ToListAsync(ct))
        .ToDictionary(s => s.Id, s => (s.Code, s.Name));

    public async Task<Dictionary<Guid, string>> RoomNamesAsync(CancellationToken ct) =>
        await db.Rooms.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);

    /// <summary>Holiday name when <paramref name="date"/> is a holiday for the campus, otherwise null.</summary>
    public async Task<string?> HolidayAsync(DateOnly date, Guid campusId, CancellationToken ct) =>
        await db.Holidays.AsNoTracking()
            .Where(h => h.StartDate <= date && h.EndDate >= date && (h.CampusId == null || h.CampusId == campusId))
            .Select(h => h.Name).FirstOrDefaultAsync(ct);

    public static bool Overlaps(TimeOnly aStart, TimeOnly aEnd, TimeOnly bStart, TimeOnly bEnd) => aStart < bEnd && bStart < aEnd;

    public static string DayName(DayOfWeek d) => d.ToString();
}
