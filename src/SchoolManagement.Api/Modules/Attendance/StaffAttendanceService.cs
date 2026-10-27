using System.Globalization;
using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Domain.Attendance;
using SchoolManagement.Infrastructure.Localization;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Attendance;

public record PunchRequest(Guid? CampusId, double? Latitude, double? Longitude);

public record DevicePunch(string UserName, DateTime TimestampUtc);

public record DevicePunchResult(int Accepted, IReadOnlyList<string> Rejected);

public record StaffDayView(Guid UserId, string Name, Guid CampusId, DateOnly Date, StaffAttendanceStatus? Status, DateTime? CheckInUtc, DateTime? CheckOutUtc,
    AttendanceSource? Source, bool? WithinGeofence, int LateMinutes, int EarlyLeaveMinutes, int OvertimeMinutes, string? LeaveType, string? Note);

public record StaffCorrection(Guid UserId, Guid CampusId, DateOnly Date, StaffAttendanceStatus Status, TimeOnly? CheckIn, TimeOnly? CheckOut, string? Note);

public record StaffMonthlyRow(Guid UserId, string Name, int SchoolDays, int Present, int Absent, int Leave, int LateDays, int LateMinutes, int EarlyLeaveMinutes, int OvertimeMinutes);

public record StaffWorkRules(TimeOnly Start, TimeOnly End, int GraceMinutes, int OvertimeAfterMinutes, (double Lat, double Lng, double Radius)? Geofence);

/// <summary>
/// ATT-06: staff check-in/out from a biometric device feed or a geofenced mobile punch, with late arrival, early
/// leaving and overtime worked out from the campus working hours. Absence is derived (a school day with no punch and no leave).
/// </summary>
public sealed class StaffAttendanceService(
    AppDbContext db,
    AcademicLookups lookups,
    SchoolDays schoolDays,
    ISettingsService settings,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<StaffDayView> PunchAsync(PunchRequest r, CancellationToken ct)
    {
        var me = currentUser.UserId ?? throw new ForbiddenException("Sign in to punch.");
        var campusId = r.CampusId ?? await HomeCampusAsync(me, ct)
                       ?? throw new DomainException("You work at more than one campus; choose the campus you are at.", "campus_required");
        if (!await WorksAtAsync(me, campusId, ct)) throw new ForbiddenException("You are not assigned to this campus.");

        var rules = await RulesAsync(campusId, ct);
        bool? within = null;
        if (rules.Geofence is { } fence)
        {
            if (r.Latitude is not { } lat || r.Longitude is not { } lng)
                throw new DomainException("Allow location access to punch from your phone.", "location_required");
            var metres = DistanceMetres(lat, lng, fence.Lat, fence.Lng);
            if (metres > fence.Radius)
                throw new DomainException($"You are about {metres:0} m from the campus; punch when you are within {fence.Radius:0} m.", "outside_geofence");
            within = true;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var row = await ApplyPunchAsync(me, campusId, now, AttendanceSource.Manual, rules, ct);
        if (row.CheckInUtc == now)
        {
            row.CheckInLatitude = r.Latitude;
            row.CheckInLongitude = r.Longitude;
            row.WithinGeofence = within;
        }
        await db.SaveChangesAsync(ct);
        return (await DayAsync(campusId, row.Date, me, ct)).First();
    }

    /// <summary>Imports punches from a biometric / RFID device (user name + time). Unknown users are reported back, not failed.</summary>
    public async Task<DevicePunchResult> ImportAsync(Guid campusId, IReadOnlyList<DevicePunch> punches, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Attendance.Staff, campusId);
        var rules = await RulesAsync(campusId, ct);
        var names = punches.Select(p => p.UserName.Trim()).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => names.Contains(u.UserName)).ToDictionaryAsync(u => u.UserName, u => u.Id, StringComparer.OrdinalIgnoreCase, ct);
        var rejected = new List<string>();
        var accepted = 0;
        foreach (var p in punches.OrderBy(p => p.TimestampUtc))
        {
            if (!users.TryGetValue(p.UserName.Trim(), out var userId)) { rejected.Add($"{p.UserName}: unknown user"); continue; }
            await ApplyPunchAsync(userId, campusId, DateTime.SpecifyKind(p.TimestampUtc, DateTimeKind.Utc), AttendanceSource.Device, rules, ct);
            accepted++;
        }
        await db.SaveChangesAsync(ct);
        return new DevicePunchResult(accepted, rejected);
    }

    /// <summary>The staff register of a campus for one day (or one person when <paramref name="userId"/> is given).</summary>
    public async Task<List<StaffDayView>> DayAsync(Guid campusId, DateOnly date, Guid? userId, CancellationToken ct)
    {
        var staff = userId is { } only
            ? await db.Users.AsNoTracking().Where(u => u.Id == only).Select(u => new { u.Id, u.FullName, u.CreatedAtUtc }).ToListAsync(ct)
            : await StaffOfCampus(campusId).Select(u => new { u.Id, u.FullName, u.CreatedAtUtc }).ToListAsync(ct);
        var ids = staff.Select(s => s.Id).ToList();
        var rows = await db.StaffAttendances.IgnoreQueryFilters().AsNoTracking().Where(a => a.Date == date && ids.Contains(a.UserId)).ToDictionaryAsync(a => a.UserId, ct);
        var leaves = await db.StaffLeaveRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(l => !l.IsDeleted && ids.Contains(l.UserId) && l.Status == LeaveStatus.Approved && l.FromDate <= date && l.ToDate >= date)
            .Select(l => new { l.UserId, Type = l.LeaveType!.Name }).ToListAsync(ct);
        var closed = await schoolDays.ClosedReasonAsync(campusId, date, ct);
        var rules = await RulesAsync(campusId, ct);
        var dayOver = date < lookups.TodayDhaka || (date == lookups.TodayDhaka && TimeOnly.FromDateTime(BanglaFormat.ToDhaka(clock.GetUtcNow().UtcDateTime)) > rules.End);

        return staff.OrderBy(s => s.FullName).Select(s =>
        {
            var row = rows.GetValueOrDefault(s.Id);
            var leave = leaves.FirstOrDefault(l => l.UserId == s.Id);
            var status = row?.Status
                         ?? (leave is not null ? StaffAttendanceStatus.Leave
                             : closed is not null ? StaffAttendanceStatus.Holiday
                             : dayOver && date >= Joined(s.CreatedAtUtc) ? StaffAttendanceStatus.Absent : (StaffAttendanceStatus?)null);
            return new StaffDayView(s.Id, s.FullName, row?.CampusId ?? campusId, date, status, row?.CheckInUtc, row?.CheckOutUtc, row?.Source, row?.WithinGeofence,
                row?.LateMinutes ?? 0, row?.EarlyLeaveMinutes ?? 0, row?.OvertimeMinutes ?? 0, leave?.Type, row?.Note ?? closed);
        }).ToList();
    }

    /// <summary>Manual correction by the attendance office (forgotten punch, device fault). Times are local.</summary>
    public async Task<StaffDayView> CorrectAsync(StaffCorrection r, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Attendance.Staff, r.CampusId);
        if (r.Date > lookups.TodayDhaka) throw new DomainException("Staff attendance cannot be recorded for a future date.", "future_date");
        if (r.CheckIn is { } i && r.CheckOut is { } o && o <= i) throw new DomainException("Check-out must be after check-in.", "bad_times");
        var rules = await RulesAsync(r.CampusId, ct);
        var row = await db.StaffAttendances.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.UserId == r.UserId && a.Date == r.Date, ct);
        if (row is null)
        {
            row = new StaffAttendance { CampusId = r.CampusId, UserId = r.UserId, Date = r.Date };
            db.StaffAttendances.Add(row);
        }
        row.Status = r.Status;
        row.Source = AttendanceSource.Manual;
        row.Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        row.CheckInUtc = r.Status == StaffAttendanceStatus.Present && r.CheckIn is { } ci ? ToUtc(r.Date, ci) : null;
        row.CheckOutUtc = r.Status == StaffAttendanceStatus.Present && r.CheckOut is { } co ? ToUtc(r.Date, co) : null;
        Recalculate(row, rules);
        await db.SaveChangesAsync(ct);
        return (await DayAsync(r.CampusId, r.Date, r.UserId, ct)).First();
    }

    /// <summary>Month summary per staff member — the input for payroll deductions and overtime (HRM).</summary>
    public async Task<List<StaffMonthlyRow>> MonthlyAsync(Guid campusId, int year, int month, CancellationToken ct)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var upTo = last < lookups.TodayDhaka ? last : lookups.TodayDhaka.AddDays(-1);
        var open = await schoolDays.BetweenAsync(campusId, first, upTo < first ? first.AddDays(-1) : upTo, ct);
        var staff = await StaffOfCampus(campusId).Select(u => new { u.Id, u.FullName, u.CreatedAtUtc }).ToListAsync(ct);
        var ids = staff.Select(s => s.Id).ToList();
        var rows = await db.StaffAttendances.IgnoreQueryFilters().AsNoTracking().Where(a => ids.Contains(a.UserId) && a.Date >= first && a.Date <= last).ToListAsync(ct);
        var leaves = await db.StaffLeaveRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(l => !l.IsDeleted && ids.Contains(l.UserId) && l.Status == LeaveStatus.Approved && l.FromDate <= last && l.ToDate >= first)
            .Select(l => new { l.UserId, l.FromDate, l.ToDate }).ToListAsync(ct);

        return staff.OrderBy(s => s.FullName).Select(s =>
        {
            var mine = rows.Where(r => r.UserId == s.Id).ToDictionary(r => r.Date);
            var joined = Joined(s.CreatedAtUtc);
            var days = open.Where(d => d >= joined).ToList();
            int present = 0, leave = 0, absent = 0;
            foreach (var d in days)
            {
                var status = mine.TryGetValue(d, out var r) ? r.Status
                    : leaves.Any(l => l.UserId == s.Id && l.FromDate <= d && l.ToDate >= d) ? StaffAttendanceStatus.Leave : StaffAttendanceStatus.Absent;
                if (status == StaffAttendanceStatus.Present) present++;
                else if (status == StaffAttendanceStatus.Leave) leave++;
                else if (status == StaffAttendanceStatus.Absent) absent++;
            }
            var worked = mine.Values.Where(r => r.Status == StaffAttendanceStatus.Present).ToList();
            return new StaffMonthlyRow(s.Id, s.FullName, days.Count, present, absent, leave, worked.Count(r => r.LateMinutes > 0), worked.Sum(r => r.LateMinutes),
                worked.Sum(r => r.EarlyLeaveMinutes), worked.Sum(r => r.OvertimeMinutes));
        }).ToList();
    }

    public async Task<StaffWorkRules> RulesAsync(Guid campusId, CancellationToken ct)
    {
        var start = await settings.GetTimeAsync(SettingKeys.StaffWorkStart, new TimeOnly(7, 30), campusId, ct);
        var end = await settings.GetTimeAsync(SettingKeys.StaffWorkEnd, new TimeOnly(14, 30), campusId, ct);
        var grace = await settings.GetIntAsync(SettingKeys.StaffLateGraceMinutes, 10, campusId, ct);
        var overtime = await settings.GetIntAsync(SettingKeys.StaffOvertimeAfterMinutes, 30, campusId, ct);
        return new StaffWorkRules(start, end, grace, overtime, ParseGeofence(await settings.GetAsync(SettingKeys.StaffGeofence, campusId, ct)));
    }

    // ------------------------------------------------------------------ internals

    /// <summary>The first punch of the day is the check-in; every later one moves the check-out.</summary>
    private async Task<StaffAttendance> ApplyPunchAsync(Guid userId, Guid campusId, DateTime utc, AttendanceSource source, StaffWorkRules rules, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(BanglaFormat.ToDhaka(utc));
        var row = db.StaffAttendances.Local.FirstOrDefault(a => a.UserId == userId && a.Date == date)
                  ?? await db.StaffAttendances.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.UserId == userId && a.Date == date, ct);
        if (row is null)
        {
            row = new StaffAttendance { CampusId = campusId, UserId = userId, Date = date };
            db.StaffAttendances.Add(row);
        }
        row.Status = StaffAttendanceStatus.Present;
        row.Source = source;
        row.StaffLeaveRequestId = null;
        if (row.CheckInUtc is null || utc < row.CheckInUtc) row.CheckInUtc = utc;
        else if (utc > row.CheckInUtc.Value.AddMinutes(1)) row.CheckOutUtc = row.CheckOutUtc is { } out_ && out_ > utc ? out_ : utc;
        Recalculate(row, rules);
        return row;
    }

    public static void Recalculate(StaffAttendance row, StaffWorkRules rules)
    {
        row.LateMinutes = row.EarlyLeaveMinutes = row.OvertimeMinutes = 0;
        if (row.Status != StaffAttendanceStatus.Present) return;
        if (row.CheckInUtc is { } inUtc)
        {
            var t = TimeOnly.FromDateTime(BanglaFormat.ToDhaka(inUtc));
            if (t > rules.Start.AddMinutes(rules.GraceMinutes)) row.LateMinutes = (int)(t - rules.Start).TotalMinutes;
        }
        if (row.CheckOutUtc is { } outUtc)
        {
            var t = TimeOnly.FromDateTime(BanglaFormat.ToDhaka(outUtc));
            if (t < rules.End) row.EarlyLeaveMinutes = (int)(rules.End - t).TotalMinutes;
            else if ((t - rules.End).TotalMinutes > rules.OvertimeAfterMinutes) row.OvertimeMinutes = (int)(t - rules.End).TotalMinutes;
        }
    }

    public static (double, double, double)? ParseGeofence(string? raw)
    {
        var parts = raw?.Split(',', StringSplitOptions.TrimEntries) ?? [];
        if (parts.Length != 3) return null;
        var ok = double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                 & double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)
                 & double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius);
        return ok && radius > 0 ? (lat, lng, radius) : null;
    }

    /// <summary>Great-circle distance (haversine).</summary>
    public static double DistanceMetres(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6_371_000;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>No absence is counted before the staff account existed.</summary>
    private static DateOnly Joined(DateTime createdAtUtc) => DateOnly.FromDateTime(BanglaFormat.ToDhaka(createdAtUtc));

    private static DateTime ToUtc(DateOnly date, TimeOnly local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(local), DateTimeKind.Unspecified), BanglaFormat.Dhaka);

    /// <summary>Active staff accounts holding a role on the campus (or organisation-wide).</summary>
    private IQueryable<Domain.Identity.User> StaffOfCampus(Guid campusId) =>
        db.Users.AsNoTracking().Where(u => u.IsActive && u.UserType == UserType.Staff
                                           && u.RoleAssignments.Any(a => a.CampusId == campusId || a.CampusId == null));

    private Task<bool> WorksAtAsync(Guid userId, Guid campusId, CancellationToken ct) =>
        db.UserRoleAssignments.AsNoTracking().AnyAsync(a => a.UserId == userId && (a.CampusId == campusId || a.CampusId == null), ct);

    /// <summary>The campus the user is assigned to, when there is exactly one.</summary>
    public async Task<Guid?> HomeCampusAsync(Guid userId, CancellationToken ct)
    {
        var campuses = await db.UserRoleAssignments.AsNoTracking().Where(a => a.UserId == userId && a.CampusId != null).Select(a => a.CampusId!.Value).Distinct().ToListAsync(ct);
        return campuses.Count == 1 ? campuses[0] : null;
    }
}
