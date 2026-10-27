using System.Net;
using System.Text;
using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Core;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Academics;

public record TimetableIssue(string Severity, string Message)
{
    public static TimetableIssue Error(string m) => new("Error", m);
    public static TimetableIssue Warning(string m) => new("Warning", m);
}

public record SlotView(Guid Id, Guid SectionId, string SectionLabel, DayOfWeek Day, int PeriodNo, string PeriodName, TimeOnly StartTime, TimeOnly EndTime,
    Guid SubjectId, string SubjectCode, string SubjectName, Guid? TeacherUserId, string? TeacherName, Guid? RoomId, string? RoomName, string? Note,
    bool IsCover = false, string? CoverNote = null);

public record PlanTeacher(Guid UserId, string Name, int Periods);

public record SubjectPlan(Guid SubjectId, string Code, string Name, SubjectType Type, Guid? GroupId, string? GroupName, int Required, int Placed,
    IReadOnlyList<PlanTeacher> Teachers, RoomType? PreferredRoomType);

public record SectionTimetable(SectionInfo Section, IReadOnlyList<DayOfWeek> Days, IReadOnlyList<PeriodView> Periods, IReadOnlyList<SlotView> Slots,
    IReadOnlyList<SubjectPlan> Plan);

public record SlotRequest(Guid SectionId, DayOfWeek Day, int PeriodNo, Guid SubjectId, Guid? TeacherUserId, Guid? RoomId, string? Note, bool Parallel = false);

public record SlotSaveResult(IReadOnlyList<SlotView> Cell, IReadOnlyList<TimetableIssue> Warnings);

public record AutoTimetableRequest(bool ClearExisting = true);

public record AutoTimetableResult(int Placed, IReadOnlyList<string> Unplaced, IReadOnlyList<TimetableIssue> Warnings);

public record PersonTimetable(Guid Id, string Name, IReadOnlyList<DayOfWeek> Days, IReadOnlyList<SlotView> Slots, int PeriodsPerWeek);

/// <summary>
/// ACA-04 timetable builder. Hard rules (refused): a teacher or room in two places at once, a break or non-teaching
/// day, two subjects in one cell unless they are electives of the same option group. Soft rules (saved with a
/// warning): more periods than planned, a teacher's daily limit or consecutive-period limit, a subject twice a day.
/// Clashes are checked by clock time, so a teacher shared between campuses with different bells is still protected.
/// </summary>
public sealed class TimetableService(AppDbContext db, AcademicLookups lookups, ISettingsService settings, ICurrentUser currentUser)
{
    private sealed record Busy(Guid SlotId, Guid SectionId, DayOfWeek Day, TimeOnly Start, TimeOnly End, Guid? TeacherId, Guid? RoomId, Guid SubjectId);

    // ------------------------------------------------------------------ reading

    public async Task<SectionTimetable> GetSectionAsync(Guid sectionId, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        if (!currentUser.CanAccessCampus(section.CampusId)) throw new ForbiddenException();
        var days = await lookups.WorkingDaysAsync(section.CampusId, ct);
        var periods = await lookups.PeriodsAsync(section.CampusId, section.ShiftId, ct);
        var slots = await db.TimetableSlots.AsNoTracking().Where(s => s.SectionId == sectionId).ToListAsync(ct);
        return new SectionTimetable(section, days, periods, await ViewsAsync(slots, ct), await PlanAsync(section, slots, ct));
    }

    public async Task<PersonTimetable> TeacherAsync(Guid userId, Guid academicYearId, CancellationToken ct)
    {
        var name = (await lookups.UserNamesAsync([userId], ct)).GetValueOrDefault(userId) ?? throw new NotFoundException("User", userId);
        var slots = await db.TimetableSlots.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.AcademicYearId == academicYearId && s.TeacherUserId == userId).ToListAsync(ct);
        var campus = slots.GroupBy(s => s.CampusId).OrderByDescending(g => g.Count()).Select(g => (Guid?)g.Key).FirstOrDefault();
        var views = await ViewsAsync(slots, ct);
        return new PersonTimetable(userId, name, await lookups.WorkingDaysAsync(campus, ct), views.OrderBy(v => v.Day).ThenBy(v => v.StartTime).ToList(), views.Count);
    }

    public async Task<PersonTimetable> RoomAsync(Guid roomId, Guid academicYearId, CancellationToken ct)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct) ?? throw new NotFoundException("Room", roomId);
        var slots = await db.TimetableSlots.AsNoTracking().Where(s => s.AcademicYearId == academicYearId && s.RoomId == roomId).ToListAsync(ct);
        var views = await ViewsAsync(slots, ct);
        return new PersonTimetable(roomId, room.Name, await lookups.WorkingDaysAsync(room.CampusId, ct), views.OrderBy(v => v.Day).ThenBy(v => v.StartTime).ToList(), views.Count);
    }

    // ------------------------------------------------------------------ editing

    public async Task<SlotSaveResult> SaveSlotAsync(SlotRequest r, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(r.SectionId, ct);
        currentUser.EnsurePermission(Permissions.Academic.Timetable, section.CampusId);
        var (period, issues) = await ValidateCellAsync(section, r.Day, r.PeriodNo, ct);
        if (period is null) throw new DomainException(issues[0].Message, "invalid_cell");

        var cell = await db.TimetableSlots.Where(s => s.SectionId == r.SectionId && s.Day == r.Day && s.PeriodNo == r.PeriodNo).ToListAsync(ct);
        var curriculum = await CurriculumAsync(section.YearGroupId, ct);
        var others = cell.Where(s => s.SubjectId != r.SubjectId).ToList();
        var replaced = new List<TimetableSlot>();
        if (others.Count > 0)
        {
            if (r.Parallel)
            {
                var group = curriculum.GetValueOrDefault(r.SubjectId)?.SubjectGroupId;
                if (group is null || others.Any(o => curriculum.GetValueOrDefault(o.SubjectId)?.SubjectGroupId != group))
                    throw new DomainException("Only electives of the same option group can share a period.", "not_option_block");
            }
            else
            {
                replaced.AddRange(others);
            }
        }

        var existing = cell.FirstOrDefault(s => s.SubjectId == r.SubjectId);
        var busy = await LoadBusyAsync(section.AcademicYearId, ct);
        var ignore = replaced.Select(s => s.Id).Append(existing?.Id ?? Guid.Empty).ToHashSet();
        var names = await lookups.SectionsAsync(busy.Select(b => b.SectionId), ct);

        var teacherClash = r.TeacherUserId is { } teacherId
            ? busy.FirstOrDefault(b => !ignore.Contains(b.SlotId) && b.TeacherId == teacherId && b.Day == r.Day && AcademicLookups.Overlaps(b.Start, b.End, period.StartTime, period.EndTime))
            : null;
        if (teacherClash is not null)
            throw new DomainException($"The teacher already has {names.GetValueOrDefault(teacherClash.SectionId)?.Label} at {teacherClash.Start:HH\\:mm}–{teacherClash.End:HH\\:mm} on {r.Day}.", "teacher_clash", isConflict: true);

        var roomClash = r.RoomId is { } roomId
            ? busy.FirstOrDefault(b => !ignore.Contains(b.SlotId) && b.RoomId == roomId && b.Day == r.Day && AcademicLookups.Overlaps(b.Start, b.End, period.StartTime, period.EndTime))
            : null;
        if (roomClash is not null)
            throw new DomainException($"The room is already used by {names.GetValueOrDefault(roomClash.SectionId)?.Label} at that time.", "room_clash", isConflict: true);

        db.TimetableSlots.RemoveRange(replaced);
        var slot = existing ?? new TimetableSlot { CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, SectionId = section.Id, Day = r.Day, PeriodNo = r.PeriodNo, SubjectId = r.SubjectId };
        if (existing is null) db.TimetableSlots.Add(slot);
        slot.TeacherUserId = r.TeacherUserId;
        slot.RoomId = r.RoomId;
        slot.Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();

        // Soft rules, evaluated as if the change were already made.
        var after = busy.Where(b => !ignore.Contains(b.SlotId)).Append(new Busy(slot.Id, section.Id, r.Day, period.StartTime, period.EndTime, r.TeacherUserId, r.RoomId, r.SubjectId)).ToList();
        var warnings = await SoftChecksAsync(section, r.SubjectId, r.TeacherUserId, r.Day, after, curriculum, ct);
        await db.SaveChangesAsync(ct);

        var saved = await db.TimetableSlots.AsNoTracking().Where(s => s.SectionId == r.SectionId && s.Day == r.Day && s.PeriodNo == r.PeriodNo).ToListAsync(ct);
        return new SlotSaveResult(await ViewsAsync(saved, ct), warnings);
    }

    public async Task DeleteSlotAsync(Guid id, CancellationToken ct)
    {
        var slot = await db.TimetableSlots.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Timetable slot", id);
        currentUser.EnsurePermission(Permissions.Academic.Timetable, slot.CampusId);
        if (await db.Substitutions.AnyAsync(s => s.TimetableSlotId == id && s.Status == SubstitutionStatus.Assigned && s.Date >= lookups.TodayDhaka, ct))
            throw new DomainException("Cover is booked for this period on an upcoming date; cancel the substitution first.", "slot_has_cover", isConflict: true);
        db.TimetableSlots.Remove(slot);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ClearSectionAsync(Guid sectionId, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        currentUser.EnsurePermission(Permissions.Academic.Timetable, section.CampusId);
        var slots = await db.TimetableSlots.Where(s => s.SectionId == sectionId).ToListAsync(ct);
        var ids = slots.Select(s => s.Id).ToList();
        if (await db.Substitutions.AnyAsync(s => ids.Contains(s.TimetableSlotId) && s.Status == SubstitutionStatus.Assigned && s.Date >= lookups.TodayDhaka, ct))
            throw new DomainException("Upcoming cover is booked for this section; cancel those substitutions first.", "slot_has_cover", isConflict: true);
        db.TimetableSlots.RemoveRange(slots);
        await db.SaveChangesAsync(ct);
        return slots.Count;
    }

    /// <summary>
    /// Fills the section's empty cells from its plan: each subject (or option block) is spread across the week, avoiding
    /// teacher and room clashes everywhere in the school and the teacher's daily and consecutive limits.
    /// </summary>
    public async Task<AutoTimetableResult> AutoAsync(Guid sectionId, AutoTimetableRequest request, CancellationToken ct)
    {
        var section = await lookups.SectionAsync(sectionId, ct);
        currentUser.EnsurePermission(Permissions.Academic.Timetable, section.CampusId);
        if (request.ClearExisting) await ClearSectionAsync(sectionId, ct);

        var days = await lookups.WorkingDaysAsync(section.CampusId, ct);
        var periods = (await lookups.PeriodsAsync(section.CampusId, section.ShiftId, ct)).Where(p => !p.IsBreak).ToList();
        if (periods.Count == 0) throw new DomainException("Set up the bell schedule for this campus first.", "no_periods");

        var existing = await db.TimetableSlots.Where(s => s.SectionId == sectionId).ToListAsync(ct);
        var plan = await PlanAsync(section, existing, ct);
        var busy = await LoadBusyAsync(section.AcademicYearId, ct);
        var maxPerDay = await settings.GetIntAsync(SettingKeys.MaxTeacherPeriodsPerDay, 6, section.CampusId, ct);
        var maxRun = await settings.GetIntAsync(SettingKeys.MaxConsecutivePeriods, 3, section.CampusId, ct);
        var rooms = await db.Rooms.AsNoTracking().Where(r => r.CampusId == section.CampusId && r.IsActive).ToListAsync(ct);
        var homeRoom = rooms.FirstOrDefault(r => section.HomeRoom != null && (r.Code == section.HomeRoom || r.Name == section.HomeRoom));

        // Units: a single subject, or every planned elective of an option group taught side by side.
        var units = plan.Where(p => p.GroupId is null).Select(p => new[] { p })
            .Concat(plan.Where(p => p.GroupId is not null).GroupBy(p => p.GroupId).Select(g => g.ToArray()))
            .Select(members => (Members: members, Remaining: members.Max(m => m.Required) - members.Min(m => m.Placed)))
            .Where(u => u.Remaining > 0)
            .OrderByDescending(u => u.Remaining)
            .ToList();

        var occupied = existing.Select(s => (s.Day, s.PeriodNo)).ToHashSet();
        var placed = 0;
        var unplaced = new List<string>();
        var perDayCount = new Dictionary<(string, DayOfWeek), int>();
        foreach (var s in existing)
        {
            var key = (UnitKey(plan, s.SubjectId), s.Day);
            perDayCount[key] = perDayCount.GetValueOrDefault(key) + 1;
        }

        foreach (var (members, remaining) in units)
        {
            var unitKey = UnitKey(plan, members[0].SubjectId);
            for (var n = 0; n < remaining; n++)
            {
                (DayOfWeek Day, PeriodView Period, List<(SubjectPlan Plan, Guid? Room)> Choice)? best = null;
                var bestScore = double.MaxValue;
                foreach (var day in days)
                {
                    var sameDay = perDayCount.GetValueOrDefault((unitKey, day));
                    foreach (var period in periods)
                    {
                        if (occupied.Contains((day, period.PeriodNo))) continue;
                        var choice = new List<(SubjectPlan, Guid?)>();
                        var ok = true;
                        foreach (var m in members)
                        {
                            var teacher = m.Teachers.FirstOrDefault()?.UserId;
                            if (teacher is { } tid && !TeacherFree(busy, tid, day, period, maxPerDay, maxRun)) { ok = false; break; }
                            choice.Add((m, PickRoom(m, rooms, homeRoom, busy, choice, day, period)));
                        }
                        if (!ok) continue;
                        // Spread across days first, then prefer earlier periods for subjects with many periods.
                        var score = sameDay * 100 + busy.Count(b => b.SectionId == sectionId && b.Day == day) + period.PeriodNo * 0.1;
                        if (score < bestScore) { bestScore = score; best = (day, period, choice); }
                    }
                }
                if (best is not { } b)
                {
                    unplaced.Add(string.Join(" / ", members.Select(m => m.Code)));
                    continue;
                }
                foreach (var (m, room) in b.Choice)
                {
                    var slot = new TimetableSlot
                    {
                        CampusId = section.CampusId, AcademicYearId = section.AcademicYearId, SectionId = sectionId, Day = b.Day, PeriodNo = b.Period.PeriodNo,
                        SubjectId = m.SubjectId, TeacherUserId = m.Teachers.FirstOrDefault()?.UserId, RoomId = room
                    };
                    db.TimetableSlots.Add(slot);
                    busy.Add(new Busy(slot.Id, sectionId, b.Day, b.Period.StartTime, b.Period.EndTime, slot.TeacherUserId, room, m.SubjectId));
                    placed++;
                }
                occupied.Add((b.Day, b.Period.PeriodNo));
                perDayCount[(unitKey, b.Day)] = perDayCount.GetValueOrDefault((unitKey, b.Day)) + 1;
            }
        }

        await db.SaveChangesAsync(ct);
        var warnings = plan.Where(p => p.Teachers.Count == 0).Select(p => TimetableIssue.Warning($"{p.Name} has no teacher allocated; it was placed without one.")).ToList();
        return new AutoTimetableResult(placed, unplaced.GroupBy(u => u).Select(g => $"{g.Key} × {g.Count()}").ToList(), warnings);
    }

    /// <summary>School-wide check for a year (and optionally one campus): clashes, unplaced periods and missing teachers.</summary>
    public async Task<IReadOnlyList<TimetableIssue>> CheckAsync(Guid academicYearId, Guid? campusId, CancellationToken ct)
    {
        var sectionQuery = db.Sections.AsNoTracking().Where(s => s.AcademicYearId == academicYearId);
        if (campusId is not null) sectionQuery = sectionQuery.Where(s => s.CampusId == campusId);
        var inScope = (await sectionQuery.Select(s => s.Id).ToListAsync(ct)).ToHashSet();

        var issues = new List<TimetableIssue>();
        var busy = await LoadBusyAsync(academicYearId, ct);
        var sections = await lookups.SectionsAsync(busy.Select(b => b.SectionId), ct);
        var names = await lookups.UserNamesAsync(busy.Select(b => b.TeacherId), ct);
        var roomNames = await lookups.RoomNamesAsync(ct);

        // Clashes are school-wide, but only reported when one of the sections involved is in scope.
        void Pairs(Func<Busy, Guid?> key, string what, Func<Guid, string> label)
        {
            foreach (var g in busy.Where(b => key(b) is not null).GroupBy(b => (key(b), b.Day)))
            {
                var list = g.OrderBy(b => b.Start).ToList();
                for (var i = 0; i < list.Count; i++)
                    for (var j = i + 1; j < list.Count && list[j].Start < list[i].End; j++)
                        if (list[i].SectionId != list[j].SectionId && (inScope.Contains(list[i].SectionId) || inScope.Contains(list[j].SectionId)))
                            issues.Add(TimetableIssue.Error($"{what} {label(key(list[i])!.Value)} is in {sections.GetValueOrDefault(list[i].SectionId)?.Label} and " +
                                                            $"{sections.GetValueOrDefault(list[j].SectionId)?.Label} on {g.Key.Day} at {list[i].Start:HH\\:mm}."));
            }
        }
        Pairs(b => b.TeacherId, "Teacher", id => names.GetValueOrDefault(id, "?"));
        Pairs(b => b.RoomId, "Room", id => roomNames.GetValueOrDefault(id, "?"));

        var withSlots = busy.Select(b => b.SectionId).ToHashSet();
        foreach (var (id, info) in await lookups.SectionsAsync(inScope.Where(withSlots.Contains), ct))
        {
            var slots = await db.TimetableSlots.AsNoTracking().Where(s => s.SectionId == id).ToListAsync(ct);
            foreach (var p in await PlanAsync(info, slots, ct))
            {
                if (p.Placed < p.Required) issues.Add(TimetableIssue.Warning($"{info.Label}: {p.Name} has {p.Placed} of {p.Required} periods."));
                if (p.Placed > p.Required) issues.Add(TimetableIssue.Warning($"{info.Label}: {p.Name} has {p.Placed} periods, {p.Required} planned."));
            }
            var noTeacher = slots.Count(s => s.TeacherUserId is null);
            if (noTeacher > 0) issues.Add(TimetableIssue.Warning($"{info.Label}: {noTeacher} period(s) have no teacher."));
        }
        return issues.Distinct().OrderBy(i => i.Severity == "Error" ? 0 : 1).ToList();
    }

    // ------------------------------------------------------------------ ACA-08 daily routine

    public record DayItem(SlotView Slot, string Status, string? Detail);

    public record MyDay(DateOnly Date, DayOfWeek Day, string? Holiday, IReadOnlyList<DayItem> Items, IReadOnlyList<ClassTeacherSection> ClassSections);

    public record ClassTeacherSection(SectionInfo Section, int Students, IReadOnlyList<SlotView> Today, IReadOnlyList<SubjectPlan> Gaps);

    /// <summary>The signed-in teacher's periods for a date, with cover they give or receive, and their class-teacher sections.</summary>
    public async Task<MyDay> MyDayAsync(Guid userId, DateOnly date, CancellationToken ct)
    {
        var year = await db.AcademicYears.AsNoTracking().Where(y => y.StartDate <= date && y.EndDate >= date).OrderByDescending(y => y.IsCurrent).FirstOrDefaultAsync(ct)
                   ?? await db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(y => y.IsCurrent, ct);
        if (year is null) return new MyDay(date, date.DayOfWeek, null, [], []);

        var mine = await db.TimetableSlots.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.AcademicYearId == year.Id && s.TeacherUserId == userId && s.Day == date.DayOfWeek).ToListAsync(ct);
        var subs = await db.Substitutions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => !s.IsDeleted && s.Date == date && s.Status == SubstitutionStatus.Assigned && (s.SubstituteUserId == userId || s.AbsentTeacherUserId == userId))
            .ToListAsync(ct);
        var coverSlotIds = subs.Where(s => s.SubstituteUserId == userId).Select(s => s.TimetableSlotId).ToList();
        var coverSlots = await db.TimetableSlots.IgnoreQueryFilters().AsNoTracking().Where(s => coverSlotIds.Contains(s.Id)).ToListAsync(ct);
        var names = await lookups.UserNamesAsync(subs.Select(s => (Guid?)s.SubstituteUserId).Concat(subs.Select(s => s.AbsentTeacherUserId)), ct);

        var items = new List<DayItem>();
        foreach (var v in await ViewsAsync(mine, ct))
        {
            var covered = subs.FirstOrDefault(s => s.TimetableSlotId == v.Id && s.AbsentTeacherUserId == userId);
            items.Add(covered is null
                ? new DayItem(v, "Teaching", null)
                : new DayItem(v, "Covered", $"Covered by {names.GetValueOrDefault(covered.SubstituteUserId)}"));
        }
        foreach (var v in await ViewsAsync(coverSlots, ct))
        {
            var s = subs.First(x => x.TimetableSlotId == v.Id && x.SubstituteUserId == userId);
            items.Add(new DayItem(v with { IsCover = true, CoverNote = s.Reason }, "Cover",
                s.AbsentTeacherUserId is { } a ? $"Covering for {names.GetValueOrDefault(a)}" : "Cover"));
        }

        var campus = mine.Select(s => (Guid?)s.CampusId).FirstOrDefault() ?? coverSlots.Select(s => (Guid?)s.CampusId).FirstOrDefault();
        var holiday = campus is { } c ? await lookups.HolidayAsync(date, c, ct) : null;

        var classSectionIds = await db.Sections.AsNoTracking().Where(s => s.AcademicYearId == year.Id && s.ClassTeacherUserId == userId).Select(s => s.Id).ToListAsync(ct);
        var classSections = new List<ClassTeacherSection>();
        foreach (var (id, info) in await lookups.SectionsAsync(classSectionIds, ct))
        {
            var slots = await db.TimetableSlots.AsNoTracking().Where(s => s.SectionId == id).ToListAsync(ct);
            var students = await db.StudentEnrolments.CountAsync(e => e.SectionId == id && e.Status == Domain.Students.EnrolmentStatus.Active, ct);
            var today = (await ViewsAsync(slots.Where(s => s.Day == date.DayOfWeek).ToList(), ct)).OrderBy(v => v.StartTime).ToList();
            var gaps = (await PlanAsync(info, slots, ct)).Where(p => p.Teachers.Count == 0 || p.Placed < p.Required).ToList();
            classSections.Add(new ClassTeacherSection(info, students, today, gaps));
        }

        return new MyDay(date, date.DayOfWeek, holiday, items.OrderBy(i => i.Slot.StartTime).ToList(), classSections);
    }

    // ------------------------------------------------------------------ printing

    public string RenderSectionHtml(SectionTimetable t, string school) =>
        RenderGrid($"{t.Section.Label} — class routine", school, t.Days, t.Periods,
            (day, p) => t.Slots.Where(s => s.Day == day && s.PeriodNo == p.PeriodNo)
                .Select(s => $"<b>{E(s.SubjectCode)}</b><br><small>{E(Initials(s.TeacherName))}{(s.RoomName is null ? "" : " · " + E(s.RoomName))}</small>"));

    public string RenderPersonHtml(PersonTimetable t, string title, string school)
    {
        var sb = Start($"{title} — {t.Name}", school);
        sb.Append("<table><tr><th>Day</th><th>Periods</th></tr>");
        foreach (var day in t.Days)
        {
            sb.Append($"<tr><th>{day}</th><td>");
            foreach (var s in t.Slots.Where(s => s.Day == day))
                sb.Append($"<div class=chip><b>{s.StartTime:HH\\:mm}–{s.EndTime:HH\\:mm}</b> {E(s.SubjectCode)} · {E(s.SectionLabel)}{(s.RoomName is null ? "" : " · " + E(s.RoomName))}</div>");
            sb.Append("</td></tr>");
        }
        sb.Append($"</table><p class=foot>{t.PeriodsPerWeek} periods a week</p></body></html>");
        return sb.ToString();
    }

    private static string RenderGrid(string title, string school, IReadOnlyList<DayOfWeek> days, IReadOnlyList<PeriodView> periods, Func<DayOfWeek, PeriodView, IEnumerable<string>> cell)
    {
        var sb = Start(title, school);
        sb.Append("<table><tr><th>Day</th>");
        foreach (var p in periods)
            sb.Append($"<th class='{(p.IsBreak ? "brk" : "")}'>{E(p.Name)}<br><small>{p.StartTime:HH\\:mm}–{p.EndTime:HH\\:mm}</small></th>");
        sb.Append("</tr>");
        foreach (var day in days)
        {
            sb.Append($"<tr><th>{day}</th>");
            foreach (var p in periods)
                sb.Append(p.IsBreak ? "<td class=brk></td>" : $"<td>{string.Join("<hr>", cell(day, p))}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private const string PrintStyle =
        "body{font-family:'Segoe UI','Noto Sans Bengali',sans-serif;margin:24px;color:#1b2a41}h1{font-size:18pt;margin:0}h2{font-size:12pt;font-weight:400;margin:2px 0 14px;color:#56657a}" +
        "table{border-collapse:collapse;width:100%}th,td{border:1px solid #c9d2df;padding:6px;text-align:center;vertical-align:middle;font-size:9.5pt}" +
        "th{background:#eef2f8}td.brk,th.brk{background:#f6f1e2;color:#8a7a4c}hr{border:0;border-top:1px dashed #c9d2df;margin:3px 0}" +
        ".chip{display:inline-block;border:1px solid #c9d2df;border-radius:6px;padding:3px 6px;margin:2px;font-size:9pt}.foot{color:#56657a;font-size:9pt}" +
        "@media print{body{margin:8mm}@page{size:A4 landscape}}";

    private static StringBuilder Start(string title, string school) =>
        new StringBuilder($"<!doctype html><html><head><meta charset='utf-8'><title>{E(title)}</title><style>{PrintStyle}</style></head><body>")
            .Append($"<h1>{E(school)}</h1><h2>{E(title)}</h2>");

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string Initials(string? name) =>
        name is null ? "—" : string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])));

    // ------------------------------------------------------------------ internals

    private async Task<(PeriodView? Period, List<TimetableIssue> Issues)> ValidateCellAsync(SectionInfo section, DayOfWeek day, int periodNo, CancellationToken ct)
    {
        var days = await lookups.WorkingDaysAsync(section.CampusId, ct);
        if (!days.Contains(day)) return (null, [TimetableIssue.Error($"{day} is not a teaching day.")]);
        var period = (await lookups.PeriodsAsync(section.CampusId, section.ShiftId, ct)).FirstOrDefault(p => p.PeriodNo == periodNo);
        if (period is null) return (null, [TimetableIssue.Error($"Period {periodNo} is not in the bell schedule.")]);
        if (period.IsBreak) return (null, [TimetableIssue.Error($"{period.Name} is a break.")]);
        return (period, []);
    }

    private async Task<Dictionary<Guid, CurriculumSubject>> CurriculumAsync(Guid yearGroupId, CancellationToken ct) =>
        await db.CurriculumSubjects.AsNoTracking().Where(c => c.YearGroupId == yearGroupId).ToDictionaryAsync(c => c.SubjectId, ct);

    /// <summary>Every slot of the year in clock time, across campuses (a teacher can teach on two campuses).</summary>
    private async Task<List<Busy>> LoadBusyAsync(Guid academicYearId, CancellationToken ct)
    {
        var slots = await db.TimetableSlots.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.AcademicYearId == academicYearId).ToListAsync(ct);
        var sections = await lookups.SectionsAsync(slots.Select(s => s.SectionId), ct);
        var result = new List<Busy>(slots.Count);
        foreach (var s in slots)
        {
            if (!sections.TryGetValue(s.SectionId, out var info)) continue;
            var p = (await lookups.PeriodsAsync(info.CampusId, info.ShiftId, ct)).FirstOrDefault(x => x.PeriodNo == s.PeriodNo);
            if (p is null) continue;
            result.Add(new Busy(s.Id, s.SectionId, s.Day, p.StartTime, p.EndTime, s.TeacherUserId, s.RoomId, s.SubjectId));
        }
        return result;
    }

    private async Task<List<SubjectPlan>> PlanAsync(SectionInfo section, IReadOnlyList<TimetableSlot> slots, CancellationToken ct)
    {
        var curriculum = await db.CurriculumSubjects.AsNoTracking().Where(c => c.YearGroupId == section.YearGroupId).OrderBy(c => c.Sequence).ToListAsync(ct);
        var groups = await db.SubjectGroups.AsNoTracking().Where(g => g.YearGroupId == section.YearGroupId).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var allocations = await db.TeacherAllocations.AsNoTracking().Where(a => a.SectionId == section.Id).ToListAsync(ct);
        var chosen = await db.StudentSubjects.AsNoTracking()
            .Where(s => s.AcademicYearId == section.AcademicYearId && db.StudentEnrolments.Any(e => e.StudentId == s.StudentId && e.SectionId == section.Id && e.Status == Domain.Students.EnrolmentStatus.Active))
            .Select(s => s.SubjectId).Distinct().ToListAsync(ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var names = await lookups.UserNamesAsync(allocations.Select(a => (Guid?)a.TeacherUserId), ct);

        var plan = new List<SubjectPlan>();
        var subjectIds = curriculum.Select(c => c.SubjectId).Concat(allocations.Select(a => a.SubjectId)).Concat(slots.Select(s => s.SubjectId)).Distinct();
        foreach (var subjectId in subjectIds)
        {
            var c = curriculum.FirstOrDefault(x => x.SubjectId == subjectId);
            var alloc = allocations.Where(a => a.SubjectId == subjectId).OrderByDescending(a => a.PeriodsPerWeek).ToList();
            var placed = slots.Count(s => s.SubjectId == subjectId);
            // Electives are planned only when this section offers them (a teacher is allocated or a student chose them).
            if (c is not null && c.Type != SubjectType.Compulsory && alloc.Count == 0 && !chosen.Contains(subjectId) && placed == 0) continue;
            var required = alloc.Count > 0 ? alloc.Sum(a => a.PeriodsPerWeek) : c?.PeriodsPerWeek ?? 0;
            var (code, name) = subjects.GetValueOrDefault(subjectId, ("?", "Unknown subject"));
            plan.Add(new SubjectPlan(subjectId, code, name, c?.Type ?? SubjectType.Optional, c?.SubjectGroupId,
                c?.SubjectGroupId is { } g ? groups.GetValueOrDefault(g) : null, required, placed,
                alloc.Select(a => new PlanTeacher(a.TeacherUserId, names.GetValueOrDefault(a.TeacherUserId, "?"), a.PeriodsPerWeek)).ToList(),
                c?.PreferredRoomType));
        }
        return plan;
    }

    private async Task<List<TimetableIssue>> SoftChecksAsync(SectionInfo section, Guid subjectId, Guid? teacherId, DayOfWeek day, List<Busy> after,
        Dictionary<Guid, CurriculumSubject> curriculum, CancellationToken ct)
    {
        var warnings = new List<TimetableIssue>();
        var subjects = await lookups.SubjectsAsync(ct);
        var subjectName = subjects.GetValueOrDefault(subjectId).Name ?? "This subject";

        if (!curriculum.ContainsKey(subjectId))
            warnings.Add(TimetableIssue.Warning($"{subjectName} is not in this class's curriculum."));

        var slots = after.Where(b => b.SectionId == section.Id).ToList();
        var allocations = await db.TeacherAllocations.AsNoTracking().Where(a => a.SectionId == section.Id && a.SubjectId == subjectId).ToListAsync(ct);
        var required = allocations.Count > 0 ? allocations.Sum(a => a.PeriodsPerWeek) : curriculum.GetValueOrDefault(subjectId)?.PeriodsPerWeek ?? 0;
        var placed = slots.Count(b => b.SubjectId == subjectId);
        if (required > 0 && placed > required)
            warnings.Add(TimetableIssue.Warning($"{subjectName} now has {placed} periods a week; {required} are planned."));
        if (slots.Count(b => b.SubjectId == subjectId && b.Day == day) > 1)
            warnings.Add(TimetableIssue.Warning($"{subjectName} is taught more than once on {day}."));

        if (teacherId is { } tid)
        {
            if (allocations.Count > 0 && allocations.All(a => a.TeacherUserId != tid))
                warnings.Add(TimetableIssue.Warning("This teacher is not allocated to teach this subject to this section."));
            var maxPerDay = await settings.GetIntAsync(SettingKeys.MaxTeacherPeriodsPerDay, 6, section.CampusId, ct);
            var maxRun = await settings.GetIntAsync(SettingKeys.MaxConsecutivePeriods, 3, section.CampusId, ct);
            var dayLoad = after.Where(b => b.TeacherId == tid && b.Day == day).OrderBy(b => b.Start).ToList();
            if (dayLoad.Count > maxPerDay)
                warnings.Add(TimetableIssue.Warning($"The teacher has {dayLoad.Count} periods on {day} (limit {maxPerDay})."));
            if (LongestRun(dayLoad) > maxRun)
                warnings.Add(TimetableIssue.Warning($"The teacher has more than {maxRun} periods in a row on {day}."));
        }
        return warnings;
    }

    /// <summary>Periods count as consecutive when the gap between them is 10 minutes or less (a real break interrupts the run).</summary>
    private static int LongestRun(IReadOnlyList<Busy> ordered)
    {
        var best = ordered.Count == 0 ? 0 : 1;
        var run = 1;
        for (var i = 1; i < ordered.Count; i++)
        {
            run = (ordered[i].Start - ordered[i - 1].End) <= TimeSpan.FromMinutes(10) ? run + 1 : 1;
            best = Math.Max(best, run);
        }
        return best;
    }

    private static bool TeacherFree(List<Busy> busy, Guid teacherId, DayOfWeek day, PeriodView period, int maxPerDay, int maxRun)
    {
        var day_ = busy.Where(b => b.TeacherId == teacherId && b.Day == day).ToList();
        if (day_.Any(b => AcademicLookups.Overlaps(b.Start, b.End, period.StartTime, period.EndTime))) return false;
        if (day_.Count >= maxPerDay) return false;
        var trial = day_.Append(new Busy(Guid.Empty, Guid.Empty, day, period.StartTime, period.EndTime, teacherId, null, Guid.Empty)).OrderBy(b => b.Start).ToList();
        return LongestRun(trial) <= maxRun;
    }

    private static Guid? PickRoom(SubjectPlan subject, List<Room> rooms, Room? home, List<Busy> busy, List<(SubjectPlan, Guid?)> taken, DayOfWeek day, PeriodView period)
    {
        bool Free(Room r) => taken.All(t => t.Item2 != r.Id) &&
                             !busy.Any(b => b.RoomId == r.Id && b.Day == day && AcademicLookups.Overlaps(b.Start, b.End, period.StartTime, period.EndTime));
        if (subject.PreferredRoomType is { } type && rooms.FirstOrDefault(r => r.Type == type && Free(r)) is { } special) return special.Id;
        if (home is not null && Free(home)) return home.Id;
        // Parallel electives need a room each; take any free classroom.
        return taken.Count > 0 ? rooms.FirstOrDefault(r => r.Type == RoomType.Classroom && Free(r))?.Id : null;
    }

    private static string UnitKey(IEnumerable<SubjectPlan> plan, Guid subjectId) =>
        plan.FirstOrDefault(p => p.SubjectId == subjectId)?.GroupId?.ToString() ?? subjectId.ToString();

    public Task<List<SlotView>> GetViewsAsync(IReadOnlyList<TimetableSlot> slots, CancellationToken ct) => ViewsAsync(slots, ct);

    private async Task<List<SlotView>> ViewsAsync(IReadOnlyList<TimetableSlot> slots, CancellationToken ct)
    {
        if (slots.Count == 0) return [];
        var sections = await lookups.SectionsAsync(slots.Select(s => s.SectionId), ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var teachers = await lookups.UserNamesAsync(slots.Select(s => s.TeacherUserId), ct);
        var rooms = await lookups.RoomNamesAsync(ct);
        var views = new List<SlotView>();
        foreach (var s in slots)
        {
            var info = sections.GetValueOrDefault(s.SectionId);
            var p = info is null ? null : (await lookups.PeriodsAsync(info.CampusId, info.ShiftId, ct)).FirstOrDefault(x => x.PeriodNo == s.PeriodNo);
            var (code, name) = subjects.GetValueOrDefault(s.SubjectId, ("?", "?"));
            views.Add(new SlotView(s.Id, s.SectionId, info?.Label ?? "?", s.Day, s.PeriodNo, p?.Name ?? $"Period {s.PeriodNo}",
                p?.StartTime ?? default, p?.EndTime ?? default, s.SubjectId, code, name, s.TeacherUserId,
                s.TeacherUserId is { } t ? teachers.GetValueOrDefault(t) : null, s.RoomId, s.RoomId is { } r ? rooms.GetValueOrDefault(r) : null, s.Note));
        }
        return views;
    }
}
