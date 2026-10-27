using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Domain.Attendance;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Notifications;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Attendance;

/// <summary>School-day rules shared by student and staff attendance.</summary>
public sealed class SchoolDays(AppDbContext db, AcademicLookups lookups)
{
    private readonly Dictionary<Guid, IReadOnlyList<DayOfWeek>> workingDays = [];

    /// <summary>Why there is no school on <paramref name="date"/> for the campus (weekend or holiday name), or null on a school day.</summary>
    public async Task<string?> ClosedReasonAsync(Guid campusId, DateOnly date, CancellationToken ct)
    {
        if (!workingDays.TryGetValue(campusId, out var days)) workingDays[campusId] = days = await lookups.WorkingDaysAsync(campusId, ct);
        if (!days.Contains(date.DayOfWeek)) return $"{date.DayOfWeek} is not a working day";
        return await lookups.HolidayAsync(date, campusId, ct) is { } holiday ? $"holiday ({holiday})" : null;
    }

    /// <summary>The school days of a campus in a date range (weekends and holidays removed).</summary>
    public async Task<List<DateOnly>> BetweenAsync(Guid campusId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!workingDays.TryGetValue(campusId, out var days)) workingDays[campusId] = days = await lookups.WorkingDaysAsync(campusId, ct);
        var holidays = await db.Holidays.IgnoreQueryFilters().AsNoTracking()
            .Where(h => !h.IsDeleted && h.StartDate <= to && h.EndDate >= from && (h.CampusId == null || h.CampusId == campusId))
            .Select(h => new { h.StartDate, h.EndDate }).ToListAsync(ct);
        var result = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
            if (days.Contains(d.DayOfWeek) && !holidays.Any(h => h.StartDate <= d && h.EndDate >= d)) result.Add(d);
        return result;
    }

    public async Task<Guid> YearForAsync(DateOnly date, CancellationToken ct) =>
        await db.AcademicYears.AsNoTracking().Where(y => y.StartDate <= date && y.EndDate >= date).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct)
        ?? throw new DomainException($"No academic year covers {date:dd MMM yyyy}.", "no_year");

    /// <summary>The student's section on a date (from the enrolment that covers it).</summary>
    public async Task<Guid?> SectionOfAsync(Guid studentId, DateOnly date, CancellationToken ct) =>
        await db.StudentEnrolments.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.StudentId == studentId && !e.IsDeleted && e.SectionId != null && e.StartDate <= date && (e.EndDate == null || e.EndDate >= date))
            .OrderByDescending(e => e.StartDate).Select(e => e.SectionId).FirstOrDefaultAsync(ct);

    public static string Code(AttendanceMark? mark) => mark switch
    {
        AttendanceMark.Present => "P",
        AttendanceMark.Absent => "A",
        AttendanceMark.Late => "L",
        AttendanceMark.Leave => "LV",
        _ => ""
    };

    /// <summary>Attendance % — present and late count as attended; approved leave counts as a day missed.</summary>
    public static decimal Percent(int attended, int marked) => marked == 0 ? 100m : Math.Round(attended * 100m / marked, 1);
}

/// <summary>
/// Sends a student-related message to the guardians (primary contacts when any are marked) by SMS and in-app.
/// <paramref name="sms"/> false keeps it in-app only; <paramref name="includeStudent"/> also tells the student's own portal account.
/// </summary>
public sealed class GuardianNotifier(AppDbContext db, INotificationService notifications)
{
    public async Task NotifyAsync(Guid studentId, Guid campusId, string templateCode, IReadOnlyDictionary<string, string?> data,
        string entityType, string entityId, CancellationToken ct, MessagePriority priority = MessagePriority.Normal, bool sms = true, bool includeStudent = false)
    {
        if (includeStudent && await db.Students.IgnoreQueryFilters().Where(s => s.Id == studentId).Select(s => s.UserId).FirstOrDefaultAsync(ct) is { } studentUser)
            await notifications.QueueAsync(new NotificationRequest
            {
                TemplateCode = templateCode, Channel = NotificationChannel.InApp, Recipient = studentUser.ToString(), RecipientUserId = studentUser,
                Data = data, Priority = priority, CampusId = campusId, RelatedEntityType = entityType, RelatedEntityId = entityId
            }, ct);

        var guardians = await db.StudentGuardians.AsNoTracking()
            .Where(g => g.StudentId == studentId && !g.Guardian!.IsDeleted)
            .Select(g => new
            {
                g.GuardianId, g.IsPrimaryContact, g.Guardian!.Phone, g.Guardian.UserId,
                Language = db.Users.IgnoreQueryFilters().Where(u => u.Id == g.Guardian.UserId).Select(u => u.PreferredLanguage).FirstOrDefault()
            })
            .ToListAsync(ct);
        var targets = guardians.Any(g => g.IsPrimaryContact) ? guardians.Where(g => g.IsPrimaryContact) : guardians;
        foreach (var g in targets)
        {
            var language = g.Language ?? "en";
            if (sms && !string.IsNullOrWhiteSpace(g.Phone))
                await notifications.QueueAsync(new NotificationRequest
                {
                    TemplateCode = templateCode, Channel = NotificationChannel.Sms, Recipient = g.Phone, RecipientGuardianId = g.GuardianId,
                    RecipientUserId = g.UserId, Data = data, Language = language, Priority = priority, CampusId = campusId,
                    RelatedEntityType = entityType, RelatedEntityId = entityId
                }, ct);
            if (g.UserId is { } userId)
                await notifications.QueueAsync(new NotificationRequest
                {
                    TemplateCode = templateCode, Channel = NotificationChannel.InApp, Recipient = userId.ToString(), RecipientGuardianId = g.GuardianId,
                    RecipientUserId = userId, Data = data, Language = language, Priority = priority, CampusId = campusId,
                    RelatedEntityType = entityType, RelatedEntityId = entityId
                }, ct);
        }
    }
}

public record TermAttendance(string? TermName, DateOnly? From, DateOnly? To, int Present, int Absent, int Late, int Leave, decimal? Percent);

/// <summary>A student's attendance history for the portals: a month calendar and the current term's totals.</summary>
public sealed class AttendanceHistory(AppDbContext db, SchoolDays schoolDays, AcademicLookups lookups)
{
    public async Task<ChildMonth> MonthAsync(Guid studentId, int? year, int? month, CancellationToken ct)
    {
        var today = lookups.TodayDhaka;
        var first = new DateOnly(year ?? today.Year, month ?? today.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var campusId = await db.Students.IgnoreQueryFilters().Where(s => s.Id == studentId).Select(s => s.CampusId).FirstAsync(ct);
        var open = (await schoolDays.BetweenAsync(campusId, first, last, ct)).ToHashSet();
        var rows = await db.StudentAttendances.IgnoreQueryFilters().AsNoTracking().Where(a => a.StudentId == studentId && a.Date >= first && a.Date <= last)
            .ToDictionaryAsync(a => a.Date, ct);
        var days = Enumerable.Range(0, last.Day).Select(i => first.AddDays(i)).Select(d =>
        {
            var r = rows.GetValueOrDefault(d);
            return new ChildDay(d, r is null ? (open.Contains(d) ? "" : "-") : SchoolDays.Code(r.Mark), open.Contains(d), r?.ArrivalTime, r?.Remarks);
        }).ToList();
        var counted = rows.Values.Where(r => r.Date <= today).ToList(); // approved leave ahead is shown, not yet counted
        int present = counted.Count(r => r.Mark == AttendanceMark.Present), absent = counted.Count(r => r.Mark == AttendanceMark.Absent),
            late = counted.Count(r => r.Mark == AttendanceMark.Late), leave = counted.Count(r => r.Mark == AttendanceMark.Leave);
        return new ChildMonth(studentId, first.Year, first.Month, days, present, absent, late, leave, SchoolDays.Percent(present + late, counted.Count));
    }

    public async Task<TermAttendance> TermAsync(Guid studentId, CancellationToken ct)
    {
        var today = lookups.TodayDhaka;
        var term = await db.Terms.IgnoreQueryFilters().AsNoTracking().Where(t => !t.IsDeleted && t.StartDate <= today && t.EndDate >= today).FirstOrDefaultAsync(ct);
        if (term is null) return new TermAttendance(null, null, null, 0, 0, 0, 0, null);
        var marks = await db.StudentAttendances.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.StudentId == studentId && a.Date >= term.StartDate && a.Date <= today).Select(a => a.Mark).ToListAsync(ct);
        int present = marks.Count(m => m == AttendanceMark.Present), late = marks.Count(m => m == AttendanceMark.Late);
        return new TermAttendance(term.Name, term.StartDate, today, present, marks.Count(m => m == AttendanceMark.Absent), late,
            marks.Count(m => m == AttendanceMark.Leave), marks.Count == 0 ? null : SchoolDays.Percent(present + late, marks.Count));
    }
}

/// <summary>Reads "HH:mm" settings.</summary>
public static class SettingTimes
{
    public static async Task<TimeOnly> GetTimeAsync(this ISettingsService settings, string key, TimeOnly fallback, Guid? campusId, CancellationToken ct) =>
        TimeOnly.TryParse(await settings.GetAsync(key, campusId, ct), System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : fallback;
}
