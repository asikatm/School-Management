using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Attendance;
using SchoolManagement.Infrastructure.Localization;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Attendance;

public record RegisterStudent(Guid StudentId, string AdmissionNo, string Name, int? RollNo, AttendanceMark? Mark, TimeOnly? ArrivalTime, string? Remarks,
    AttendanceSource? Source, bool OnApprovedLeave);

public record RegisterPeriod(int PeriodNo, string Name, TimeOnly StartTime, TimeOnly EndTime, string? SubjectName, bool Taken);

public record RegisterView(Guid SectionId, string SectionLabel, Guid CampusId, DateOnly Date, int? PeriodNo, string? ClosedReason, bool Taken, string? TakenBy,
    DateTime? TakenAtUtc, bool CanEdit, string? EditBlockedReason, bool PeriodWise, IReadOnlyList<RegisterPeriod> Periods, IReadOnlyList<RegisterStudent> Students);

public record MarkEntry(Guid StudentId, AttendanceMark Mark, TimeOnly? ArrivalTime, string? Remarks);

public record SaveRegisterRequest(Guid SectionId, DateOnly Date, int? PeriodNo, IReadOnlyList<MarkEntry> Entries);

public record RegisterSummary(Guid SectionId, string SectionLabel, int? PeriodNo, int Present, int Absent, int Late, int OnLeave, string? TakenBy, DateTime TakenAtUtc);

public record PendingRegister(Guid SectionId, string SectionLabel, string? ClassTeacher, int Students);

public record DailyOverview(DateOnly Date, string? ClosedReason, int Sections, int Taken, int Present, int Absent, int Late, int OnLeave,
    IReadOnlyList<PendingRegister> NotTaken, IReadOnlyList<RegisterSummary> Registers);

public record MonthlyRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo, IReadOnlyList<string> Days, int Present, int Absent, int Late, int Leave, decimal Percent);

public record MonthlyRegister(Guid SectionId, string SectionLabel, int Year, int Month, IReadOnlyList<DateOnly> Days, IReadOnlyList<bool> SchoolDays, IReadOnlyList<MonthlyRow> Students);

public record ThresholdRow(Guid StudentId, Guid CampusId, string AdmissionNo, string Name, string ClassName, int Marked, int Attended, int Absent, int Leave,
    decimal Percent, WarningLevel? Level, DateTime? WarnedAtUtc);

public record ThresholdReport(Guid TermId, string TermName, DateOnly From, DateOnly To, decimal WarningPercent, decimal MinimumPercent, IReadOnlyList<ThresholdRow> Students);

/// <summary>
/// ATT-01/02/05: daily and period registers (with the edit window and own-class rule), the daily overview with
/// registers not yet taken, the monthly register and the term attendance-threshold report.
/// </summary>
public sealed class AttendanceService(
    AppDbContext db,
    AcademicLookups lookups,
    SchoolDays schoolDays,
    ISettingsService settings,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    private sealed record RosterRow(Guid StudentId, string AdmissionNo, string Name, int? RollNo);

    public async Task<RegisterView> GetAsync(Guid sectionId, DateOnly date, int? periodNo, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        EnsureCampus(section.CampusId);
        var roster = await RosterAsync(section, date, ct);
        var ids = roster.Select(r => r.StudentId).ToList();
        var periodWise = await IsPeriodWiseAsync(section, ct);
        if (periodNo is not null && !periodWise) throw new DomainException("This class takes daily attendance only.", "not_period_wise");

        var daily = await db.StudentAttendances.AsNoTracking().Where(a => a.Date == date && ids.Contains(a.StudentId)).ToDictionaryAsync(a => a.StudentId, ct);
        var onLeave = (await db.StudentLeaveRequests.AsNoTracking()
            .Where(l => ids.Contains(l.StudentId) && l.Status == LeaveStatus.Approved && l.FromDate <= date && l.ToDate >= date)
            .Select(l => l.StudentId).ToListAsync(ct)).ToHashSet();

        Dictionary<Guid, PeriodAttendance> periodRows = [];
        if (periodNo is { } p)
            periodRows = await db.PeriodAttendances.AsNoTracking().Where(a => a.Date == date && a.PeriodNo == p && ids.Contains(a.StudentId)).ToDictionaryAsync(a => a.StudentId, ct);

        var students = roster.Select(r =>
        {
            var d = daily.GetValueOrDefault(r.StudentId);
            if (periodNo is null)
                return new RegisterStudent(r.StudentId, r.AdmissionNo, r.Name, r.RollNo, d?.Mark ?? (onLeave.Contains(r.StudentId) ? AttendanceMark.Leave : null),
                    d?.ArrivalTime, d?.Remarks, d?.Source, onLeave.Contains(r.StudentId));
            // A period register starts from the day's mark for students who are away all day.
            var pr = periodRows.GetValueOrDefault(r.StudentId);
            var carried = d?.Mark is AttendanceMark.Absent or AttendanceMark.Leave ? d.Mark : (onLeave.Contains(r.StudentId) ? AttendanceMark.Leave : (AttendanceMark?)null);
            return new RegisterStudent(r.StudentId, r.AdmissionNo, r.Name, r.RollNo, pr?.Mark ?? carried, null, null, pr is null ? null : AttendanceSource.Manual, onLeave.Contains(r.StudentId));
        }).ToList();

        var registers = await db.AttendanceRegisters.AsNoTracking().Where(x => x.SectionId == sectionId && x.Date == date).ToListAsync(ct);
        var register = registers.FirstOrDefault(x => x.PeriodNo == periodNo);
        var takenBy = register?.TakenByUserId is { } by ? (await lookups.UserNamesAsync([by], ct)).GetValueOrDefault(by) : null;
        var closed = await schoolDays.ClosedReasonAsync(section.CampusId, date, ct);
        var (canEdit, why) = closed is not null ? (false, $"No school: {closed}.")
            : date > lookups.TodayDhaka ? (false, "Attendance cannot be taken for a future date.")
            : await EditRightsAsync(section, date, register, ct);

        var periods = new List<RegisterPeriod>();
        if (periodWise)
        {
            var slots = await db.TimetableSlots.AsNoTracking().Where(s => s.SectionId == sectionId && s.Day == date.DayOfWeek).ToListAsync(ct);
            var subjects = await lookups.SubjectsAsync(ct);
            foreach (var pv in (await lookups.PeriodsAsync(section.CampusId, section.ShiftId, ct)).Where(x => !x.IsBreak))
            {
                var slot = slots.FirstOrDefault(s => s.PeriodNo == pv.PeriodNo);
                periods.Add(new RegisterPeriod(pv.PeriodNo, pv.Name, pv.StartTime, pv.EndTime, slot is null ? null : subjects.GetValueOrDefault(slot.SubjectId).Name,
                    registers.Any(x => x.PeriodNo == pv.PeriodNo)));
            }
        }

        return new RegisterView(section.Id, section.Label, section.CampusId, date, periodNo, closed, register is not null, takenBy, register?.TakenAtUtc,
            canEdit, why, periodWise, periods, students);
    }

    public async Task<RegisterView> SaveAsync(SaveRegisterRequest r, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(r.SectionId, ct);
        EnsureCampus(section.CampusId);
        if (r.Date > lookups.TodayDhaka) throw new DomainException("Attendance cannot be taken for a future date.", "future_date");
        if (await schoolDays.ClosedReasonAsync(section.CampusId, r.Date, ct) is { } closed)
            throw new DomainException($"There is no register on {r.Date:dd MMM yyyy}: {closed}.", "not_school_day");
        if (r.PeriodNo is not null && !await IsPeriodWiseAsync(section, ct))
            throw new DomainException("This class takes daily attendance only.", "not_period_wise");

        var register = await db.AttendanceRegisters.FirstOrDefaultAsync(x => x.SectionId == r.SectionId && x.Date == r.Date && x.PeriodNo == r.PeriodNo, ct);
        var (canEdit, why) = await EditRightsAsync(section, r.Date, register, ct);
        if (!canEdit) throw new ForbiddenException(why ?? "You cannot change this register.");

        var roster = await RosterAsync(section, r.Date, ct);
        var ids = roster.Select(x => x.StudentId).ToHashSet();
        var unknown = r.Entries.Where(e => !ids.Contains(e.StudentId)).ToList();
        if (unknown.Count > 0) throw new DomainException($"{unknown.Count} student(s) in the request are not in this class on {r.Date:dd MMM yyyy}.", "not_in_class");
        var missing = roster.Where(x => r.Entries.All(e => e.StudentId != x.StudentId)).Select(x => x.Name).ToList();
        if (missing.Count > 0) throw new DomainException("Mark every student: " + string.Join(", ", missing.Take(5)) + (missing.Count > 5 ? $" and {missing.Count - 5} more" : "") + ".", "incomplete_register");
        if (r.Entries.GroupBy(e => e.StudentId).Any(g => g.Count() > 1)) throw new DomainException("A student is listed twice.", "duplicate_student");

        if (r.PeriodNo is { } periodNo)
        {
            var subjectId = await db.TimetableSlots.AsNoTracking()
                .Where(s => s.SectionId == section.Id && s.Day == r.Date.DayOfWeek && s.PeriodNo == periodNo).Select(s => (Guid?)s.SubjectId).FirstOrDefaultAsync(ct);
            var existing = await db.PeriodAttendances.Where(a => a.Date == r.Date && a.PeriodNo == periodNo && ids.Contains(a.StudentId)).ToDictionaryAsync(a => a.StudentId, ct);
            foreach (var e in r.Entries)
            {
                if (!existing.TryGetValue(e.StudentId, out var row))
                {
                    row = new PeriodAttendance { CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, StudentId = e.StudentId, Date = r.Date, PeriodNo = periodNo };
                    db.PeriodAttendances.Add(row);
                }
                row.SectionId = section.Id;
                row.SubjectId = subjectId;
                row.Mark = e.Mark;
            }
        }
        else
        {
            var existing = await db.StudentAttendances.Where(a => a.Date == r.Date && ids.Contains(a.StudentId)).ToDictionaryAsync(a => a.StudentId, ct);
            foreach (var e in r.Entries)
            {
                if (!existing.TryGetValue(e.StudentId, out var row))
                {
                    row = new StudentAttendance { CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, StudentId = e.StudentId, Date = r.Date };
                    db.StudentAttendances.Add(row);
                }
                // A leave mark placed by an approved application stays linked to it while the teacher keeps it.
                if (!(row.Source == AttendanceSource.Leave && e.Mark == AttendanceMark.Leave))
                {
                    row.Source = AttendanceSource.Manual;
                    row.StudentLeaveRequestId = null;
                }
                row.SectionId = section.Id;
                row.Mark = e.Mark;
                row.ArrivalTime = e.Mark == AttendanceMark.Late ? e.ArrivalTime : null;
                row.Remarks = string.IsNullOrWhiteSpace(e.Remarks) ? null : e.Remarks.Trim();
            }
        }

        if (register is null)
        {
            register = new AttendanceRegister { CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, SectionId = section.Id, Date = r.Date, PeriodNo = r.PeriodNo };
            db.AttendanceRegisters.Add(register);
        }
        register.TakenByUserId = currentUser.UserId;
        register.TakenAtUtc = clock.GetUtcNow().UtcDateTime;
        register.Present = r.Entries.Count(e => e.Mark == AttendanceMark.Present);
        register.Absent = r.Entries.Count(e => e.Mark == AttendanceMark.Absent);
        register.Late = r.Entries.Count(e => e.Mark == AttendanceMark.Late);
        register.OnLeave = r.Entries.Count(e => e.Mark == AttendanceMark.Leave);

        await db.SaveChangesAsync(ct);
        return await GetAsync(section.Id, r.Date, r.PeriodNo, ct);
    }

    /// <summary>ATT-01: the day across a campus — totals, the registers taken and the classes still missing one.</summary>
    public async Task<DailyOverview> OverviewAsync(DateOnly date, Guid? campusId, CancellationToken ct)
    {
        var yearId = await schoolDays.YearForAsync(date, ct);
        var q = db.Sections.AsNoTracking().Where(s => s.AcademicYearId == yearId);
        if (campusId is not null) q = q.Where(s => s.CampusId == campusId);
        var sectionIds = await q.Select(s => s.Id).ToListAsync(ct);
        var sections = await lookups.SectionsAsync(sectionIds, ct);

        var registers = await db.AttendanceRegisters.AsNoTracking().Where(x => x.Date == date && sectionIds.Contains(x.SectionId)).ToListAsync(ct);
        var names = await lookups.UserNamesAsync(registers.Select(x => x.TakenByUserId).Concat(sections.Values.Select(s => s.ClassTeacherUserId)), ct);
        var counts = await db.StudentEnrolments.AsNoTracking()
            .Where(e => e.SectionId != null && sectionIds.Contains(e.SectionId.Value) && e.StartDate <= date && (e.EndDate == null || e.EndDate >= date))
            .GroupBy(e => e.SectionId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // Sections with no students (or closed for the day on their campus) are not "missing" a register.
        var open = new List<SectionInfo>();
        foreach (var s in sections.Values)
            if (counts.GetValueOrDefault(s.Id) > 0 && await schoolDays.ClosedReasonAsync(s.CampusId, date, ct) is null) open.Add(s);
        var daily = registers.Where(x => x.PeriodNo == null).ToList();
        var notTaken = open.Where(s => daily.All(x => x.SectionId != s.Id)).OrderBy(s => s.Label)
            .Select(s => new PendingRegister(s.Id, s.Label, s.ClassTeacherUserId is { } t ? names.GetValueOrDefault(t) : null, counts.GetValueOrDefault(s.Id))).ToList();
        var summaries = registers.OrderBy(x => sections.GetValueOrDefault(x.SectionId)?.Label).ThenBy(x => x.PeriodNo)
            .Select(x => new RegisterSummary(x.SectionId, sections.GetValueOrDefault(x.SectionId)?.Label ?? "?", x.PeriodNo, x.Present, x.Absent, x.Late, x.OnLeave,
                x.TakenByUserId is { } by ? names.GetValueOrDefault(by) : null, x.TakenAtUtc)).ToList();

        var closed = campusId is { } c ? await schoolDays.ClosedReasonAsync(c, date, ct) : null;
        return new DailyOverview(date, closed, open.Count, open.Count - notTaken.Count, daily.Sum(x => x.Present), daily.Sum(x => x.Absent), daily.Sum(x => x.Late),
            daily.Sum(x => x.OnLeave), notTaken, summaries);
    }

    /// <summary>ATT-02: the month grid for a section, one row per student with daily codes and totals.</summary>
    public async Task<MonthlyRegister> MonthlyAsync(Guid sectionId, int year, int month, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        EnsureCampus(section.CampusId);
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var days = Enumerable.Range(0, last.Day).Select(i => first.AddDays(i)).ToList();
        var open = (await schoolDays.BetweenAsync(section.CampusId, first, last, ct)).ToHashSet();
        var today = lookups.TodayDhaka;

        var roster = await db.StudentEnrolments.AsNoTracking()
            .Where(e => e.SectionId == sectionId && e.StartDate <= last && (e.EndDate == null || e.EndDate >= first))
            .Select(e => new RosterRow(e.StudentId, e.Student!.AdmissionNo, e.Student.FirstName + (e.Student.LastName == null ? "" : " " + e.Student.LastName), e.RollNo))
            .ToListAsync(ct);
        roster = roster.DistinctBy(r => r.StudentId).OrderBy(r => r.RollNo ?? int.MaxValue).ThenBy(r => r.Name).ToList();
        var ids = roster.Select(r => r.StudentId).ToList();
        var marks = await db.StudentAttendances.AsNoTracking().Where(a => ids.Contains(a.StudentId) && a.Date >= first && a.Date <= last)
            .Select(a => new { a.StudentId, a.Date, a.Mark }).ToListAsync(ct);
        var byStudent = marks.ToLookup(m => m.StudentId);

        var rows = roster.Select(r =>
        {
            var mine = byStudent[r.StudentId].ToDictionary(m => m.Date, m => m.Mark);
            var codes = days.Select(d => mine.TryGetValue(d, out var m) ? SchoolDays.Code(m) : open.Contains(d) ? "" : "-").ToList();
            // Leave approved for coming days shows in the grid but does not count until the day has passed.
            var counted = mine.Where(m => m.Key <= today).Select(m => m.Value).ToList();
            int present = counted.Count(m => m == AttendanceMark.Present), absent = counted.Count(m => m == AttendanceMark.Absent),
                late = counted.Count(m => m == AttendanceMark.Late), leave = counted.Count(m => m == AttendanceMark.Leave);
            return new MonthlyRow(r.StudentId, r.AdmissionNo, r.Name, r.RollNo, codes, present, absent, late, leave, SchoolDays.Percent(present + late, counted.Count));
        }).ToList();
        return new MonthlyRegister(section.Id, section.Label, year, month, days, days.Select(open.Contains).ToList(), rows);
    }

    /// <summary>ATT-05: term attendance % per student, flagged against the warning and minimum thresholds.</summary>
    public async Task<ThresholdReport> ThresholdsAsync(Guid? termId, Guid? campusId, Guid? sectionId, bool belowOnly, CancellationToken ct)
    {
        var today = lookups.TodayDhaka;
        var term = termId is { } id
            ? await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Term", id)
            : await db.Terms.AsNoTracking().Where(t => t.StartDate <= today && t.EndDate >= today).FirstOrDefaultAsync(ct)
              ?? await db.Terms.AsNoTracking().Where(t => t.StartDate <= today).OrderByDescending(t => t.StartDate).FirstOrDefaultAsync(ct)
              ?? throw new DomainException("No term has started yet.", "no_term");
        var to = term.EndDate < today ? term.EndDate : today;
        var warn = await settings.GetDecimalAsync(SettingKeys.AttendanceWarningPercent, 85, campusId, ct);
        var min = await settings.GetDecimalAsync(SettingKeys.AttendanceMinimumPercent, 75, campusId, ct);

        var marks = db.StudentAttendances.AsNoTracking().Where(a => a.Date >= term.StartDate && a.Date <= to);
        if (campusId is not null) marks = marks.Where(a => a.CampusId == campusId);
        if (sectionId is not null) marks = marks.Where(a => a.SectionId == sectionId);
        var flat = await marks.Select(a => new { a.StudentId, a.CampusId, a.SectionId, a.Date, a.Mark }).ToListAsync(ct);
        var totals = flat.GroupBy(a => new { a.StudentId, a.CampusId })
            .Select(g => new
            {
                g.Key.StudentId, g.Key.CampusId, Marked = g.Count(),
                Attended = g.Count(a => a.Mark is AttendanceMark.Present or AttendanceMark.Late),
                Absent = g.Count(a => a.Mark == AttendanceMark.Absent),
                Leave = g.Count(a => a.Mark == AttendanceMark.Leave),
                g.MaxBy(a => a.Date)!.SectionId
            }).ToList();

        var ids = totals.Select(t => t.StudentId).ToList();
        var students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.AdmissionNo, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName) }).ToDictionaryAsync(s => s.Id, ct);
        var sections = await lookups.SectionsAsync(totals.Select(t => t.SectionId), ct);
        var warnings = await db.AttendanceWarnings.AsNoTracking().Where(w => w.TermId == term.Id && ids.Contains(w.StudentId)).ToListAsync(ct);

        var rows = totals.Where(t => students.ContainsKey(t.StudentId)).Select(t =>
        {
            var pct = SchoolDays.Percent(t.Attended, t.Marked);
            WarningLevel? level = pct < min ? WarningLevel.BelowMinimum : pct < warn ? WarningLevel.Caution : null;
            var sent = warnings.Where(w => w.StudentId == t.StudentId).OrderByDescending(w => w.SentAtUtc).FirstOrDefault();
            var s = students[t.StudentId];
            return new ThresholdRow(t.StudentId, t.CampusId, s.AdmissionNo, s.Name, sections.GetValueOrDefault(t.SectionId)?.Label ?? "?", t.Marked, t.Attended, t.Absent, t.Leave,
                pct, level, sent?.SentAtUtc);
        })
            .Where(r => !belowOnly || r.Level is not null)
            .OrderBy(r => r.Percent).ThenBy(r => r.ClassName).ThenBy(r => r.Name).ToList();
        return new ThresholdReport(term.Id, term.Name, term.StartDate, to, warn, min, rows);
    }

    // ------------------------------------------------------------------ internals

    private void EnsureCampus(Guid campusId)
    {
        if (!currentUser.CanAccessCampus(campusId)) throw new ForbiddenException("You do not have access to this campus.");
    }

    private async Task<List<RosterRow>> RosterAsync(SectionInfo section, DateOnly date, CancellationToken ct)
    {
        var rows = await db.StudentEnrolments.AsNoTracking()
            .Where(e => e.SectionId == section.Id && e.StartDate <= date && (e.EndDate == null || e.EndDate >= date))
            .Select(e => new RosterRow(e.StudentId, e.Student!.AdmissionNo, e.Student.FirstName + (e.Student.LastName == null ? "" : " " + e.Student.LastName), e.RollNo))
            .ToListAsync(ct);
        return rows.DistinctBy(r => r.StudentId).OrderBy(r => r.RollNo ?? int.MaxValue).ThenBy(r => r.Name).ToList();
    }

    /// <summary>Senior programmes (setting <c>Attendance:PeriodWiseProgrammes</c>) also take attendance every period.</summary>
    private async Task<bool> IsPeriodWiseAsync(SectionInfo section, CancellationToken ct)
    {
        var raw = await settings.GetAsync(SettingKeys.PeriodWiseProgrammes, section.CampusId, ct);
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var codes = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var programme = await db.YearGroups.IgnoreQueryFilters().AsNoTracking().Where(y => y.Id == section.YearGroupId).Select(y => y.Programme!.Code).FirstOrDefaultAsync(ct);
        return programme is not null && codes.Contains(programme, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attendance-editors may change any register at any time. Teachers may take the registers of classes they
    /// teach (class teacher, allocated subject teacher or today's cover), and change them for the edit window after the day.
    /// </summary>
    private async Task<(bool, string?)> EditRightsAsync(SectionInfo section, DateOnly date, AttendanceRegister? register, CancellationToken ct)
    {
        if (currentUser.HasPermission(Permissions.Attendance.Edit, section.CampusId)) return (true, null);
        if (!currentUser.HasPermission(Permissions.Attendance.Take, section.CampusId)) return (false, "You can view this register but not take it.");
        if (currentUser.UserId is not { } me || !await TeachesAsync(section, me, date, ct))
            return (false, "Only the class teacher or a teacher of this class can take its register.");

        var hours = await settings.GetIntAsync(SettingKeys.AttendanceEditWindowHours, 48, section.CampusId, ct);
        var deadline = date.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(hours);
        if (BanglaFormat.ToDhaka(clock.GetUtcNow().UtcDateTime) > deadline)
            return (false, $"The register can only be changed within {hours} hours of the day; ask an attendance editor.");
        if (register?.TakenByUserId is { } takenBy && takenBy != me && section.ClassTeacherUserId != me)
            return (false, "Another teacher took this register; only they or the class teacher can change it.");
        return (true, null);
    }

    private async Task<bool> TeachesAsync(SectionInfo section, Guid userId, DateOnly date, CancellationToken ct) =>
        section.ClassTeacherUserId == userId
        || await db.TeacherAllocations.IgnoreQueryFilters().AsNoTracking().AnyAsync(a => a.SectionId == section.Id && a.TeacherUserId == userId, ct)
        || await db.Substitutions.AsNoTracking().AnyAsync(s => s.SectionId == section.Id && s.Date == date && s.SubstituteUserId == userId && s.Status == SubstitutionStatus.Assigned, ct);
}
