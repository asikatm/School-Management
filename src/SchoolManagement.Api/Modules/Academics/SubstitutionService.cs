using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Notifications;

namespace SchoolManagement.Api.Modules.Academics;

public record CoverCandidate(Guid UserId, string Name, bool TeachesSubject, int CoversThisWeek, int PeriodsThatDay);

public record CoverNeed(SlotView Slot, Guid? SubstitutionId, Guid? SubstituteUserId, string? SubstituteName, string? Reason, IReadOnlyList<CoverCandidate> Candidates);

public record CoverNeeds(DateOnly Date, DayOfWeek Day, Guid TeacherUserId, string TeacherName, string? Holiday, IReadOnlyList<CoverNeed> Periods);

public record AssignCoverRequest(Guid TimetableSlotId, DateOnly Date, Guid SubstituteUserId, string? Reason);

public record SubstitutionView(Guid Id, DateOnly Date, string PeriodName, TimeOnly StartTime, TimeOnly EndTime, string SectionLabel, string SubjectName,
    Guid? AbsentTeacherUserId, string? AbsentTeacherName, Guid SubstituteUserId, string SubstituteName, string? Reason, SubstitutionStatus Status);

public record CoverReportRow(Guid UserId, string Name, int CoversGiven, int PeriodsCoveredForThem);

/// <summary>
/// ACA-05: when a teacher is absent, lists their periods for the day with free teachers ranked by fit (teaches the
/// subject, fewest covers this week, lightest day), books the cover, notifies the substitute and keeps the record
/// for payroll (HRM-04 cover-period pay).
/// </summary>
public sealed class SubstitutionService(
    AppDbContext db,
    AcademicLookups lookups,
    TimetableService timetable,
    INotificationService notifications,
    ICurrentUser currentUser)
{
    public async Task<CoverNeeds> NeedsAsync(Guid teacherUserId, DateOnly date, CancellationToken ct)
    {
        var name = (await lookups.UserNamesAsync([teacherUserId], ct)).GetValueOrDefault(teacherUserId) ?? throw new NotFoundException("User", teacherUserId);
        var year = await YearForAsync(date, ct);
        var slots = await db.TimetableSlots.AsNoTracking()
            .Where(s => s.AcademicYearId == year && s.TeacherUserId == teacherUserId && s.Day == date.DayOfWeek).ToListAsync(ct);
        var views = (await timetable.GetViewsAsync(slots, ct)).OrderBy(v => v.StartTime).ToList();
        var holiday = slots.Count > 0 ? await lookups.HolidayAsync(date, slots[0].CampusId, ct) : null;

        var existing = await db.Substitutions.AsNoTracking()
            .Where(s => s.Date == date && s.Status == SubstitutionStatus.Assigned && slots.Select(x => x.Id).Contains(s.TimetableSlotId)).ToListAsync(ct);
        var names = await lookups.UserNamesAsync(existing.Select(e => (Guid?)e.SubstituteUserId), ct);

        var needs = new List<CoverNeed>();
        foreach (var v in views)
        {
            var booked = existing.FirstOrDefault(e => e.TimetableSlotId == v.Id);
            var candidates = holiday is null ? await CandidatesAsync(slots.First(s => s.Id == v.Id), v, date, teacherUserId, ct) : [];
            needs.Add(new CoverNeed(v, booked?.Id, booked?.SubstituteUserId, booked is null ? null : names.GetValueOrDefault(booked.SubstituteUserId), booked?.Reason, candidates));
        }
        return new CoverNeeds(date, date.DayOfWeek, teacherUserId, name, holiday, needs);
    }

    public async Task<SubstitutionView> AssignAsync(AssignCoverRequest r, CancellationToken ct)
    {
        var slot = await db.TimetableSlots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.TimetableSlotId, ct) ?? throw new NotFoundException("Timetable slot", r.TimetableSlotId);
        currentUser.EnsurePermission(Permissions.Academic.Substitutions, slot.CampusId);
        if (slot.Day != r.Date.DayOfWeek) throw new DomainException($"That period is on {slot.Day}, but {r.Date:dd MMM yyyy} is a {r.Date.DayOfWeek}.", "wrong_day");
        if (await lookups.HolidayAsync(r.Date, slot.CampusId, ct) is { } holiday) throw new DomainException($"{r.Date:dd MMM yyyy} is a holiday ({holiday}).", "holiday");
        if (r.SubstituteUserId == slot.TeacherUserId) throw new DomainException("The substitute must be someone other than the absent teacher.", "same_teacher");
        if (await db.Substitutions.AnyAsync(s => s.TimetableSlotId == slot.Id && s.Date == r.Date && s.Status == SubstitutionStatus.Assigned, ct))
            throw new DomainException("Cover is already booked for this period; cancel it first to change the substitute.", "cover_exists", isConflict: true);

        var view = (await timetable.GetViewsAsync([slot], ct))[0];
        var candidates = await CandidatesAsync(slot, view, r.Date, slot.TeacherUserId, ct, includeBusy: true);
        var pick = candidates.FirstOrDefault(c => c.UserId == r.SubstituteUserId);
        if (pick is null || await IsBusyAsync(r.SubstituteUserId, slot.AcademicYearId, r.Date, view, ct))
            throw new DomainException("That teacher is not free at this time.", "substitute_busy", isConflict: true);

        var sub = new Substitution
        {
            CampusId = slot.CampusId, Date = r.Date, TimetableSlotId = slot.Id, SectionId = slot.SectionId, PeriodNo = slot.PeriodNo, SubjectId = slot.SubjectId,
            AbsentTeacherUserId = slot.TeacherUserId, SubstituteUserId = r.SubstituteUserId, Reason = string.IsNullOrWhiteSpace(r.Reason) ? null : r.Reason.Trim()
        };
        db.Substitutions.Add(sub);
        await NotifyAsync(sub, view, ct);
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([sub], ct))[0];
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var sub = await db.Substitutions.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Substitution", id);
        currentUser.EnsurePermission(Permissions.Academic.Substitutions, sub.CampusId);
        sub.Status = SubstitutionStatus.Cancelled;
        notifications.QueueRaw(NotificationChannel.InApp, sub.SubstituteUserId.ToString(),
            $"Cover on {sub.Date:dd MMM yyyy}, period {sub.PeriodNo} has been cancelled.", "Cover cancelled", campusId: sub.CampusId, recipientUserId: sub.SubstituteUserId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<SubstitutionView>> ListAsync(DateOnly from, DateOnly to, Guid? campusId, CancellationToken ct)
    {
        var q = db.Substitutions.AsNoTracking().Where(s => s.Date >= from && s.Date <= to);
        if (campusId is not null) q = q.Where(s => s.CampusId == campusId);
        return (await ViewsAsync(await q.OrderBy(s => s.Date).ThenBy(s => s.PeriodNo).ToListAsync(ct), ct)).ToList();
    }

    /// <summary>Covers given and received per teacher in a period — the input for cover-period pay.</summary>
    public async Task<List<CoverReportRow>> ReportAsync(DateOnly from, DateOnly to, Guid? campusId, CancellationToken ct)
    {
        var q = db.Substitutions.AsNoTracking().Where(s => s.Date >= from && s.Date <= to && s.Status == SubstitutionStatus.Assigned);
        if (campusId is not null) q = q.Where(s => s.CampusId == campusId);
        var rows = await q.Select(s => new { s.SubstituteUserId, s.AbsentTeacherUserId }).ToListAsync(ct);
        var ids = rows.Select(r => (Guid?)r.SubstituteUserId).Concat(rows.Select(r => r.AbsentTeacherUserId)).OfType<Guid>().Distinct().ToList();
        var names = await lookups.UserNamesAsync(ids.Select(i => (Guid?)i), ct);
        return ids.Select(id => new CoverReportRow(id, names.GetValueOrDefault(id, "?"), rows.Count(r => r.SubstituteUserId == id), rows.Count(r => r.AbsentTeacherUserId == id)))
            .OrderByDescending(r => r.CoversGiven).ThenBy(r => r.Name).ToList();
    }

    // ------------------------------------------------------------------ internals

    private async Task<Guid> YearForAsync(DateOnly date, CancellationToken ct) =>
        await db.AcademicYears.AsNoTracking().Where(y => y.StartDate <= date && y.EndDate >= date).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct)
        ?? await db.AcademicYears.AsNoTracking().Where(y => y.IsCurrent).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct)
        ?? throw new DomainException("No academic year covers that date.", "no_year");

    /// <summary>
    /// Teachers who could take the period: anyone teaching on the campus this year or holding a teaching role there,
    /// except the absent teacher and anyone absent or busy at that time.
    /// </summary>
    private async Task<List<CoverCandidate>> CandidatesAsync(TimetableSlot slot, SlotView view, DateOnly date, Guid? absentTeacher, CancellationToken ct, bool includeBusy = false)
    {
        var teachingRoles = new[] { RoleNames.ClassTeacher, RoleNames.SubjectTeacher, RoleNames.HeadOfSection };
        var fromRoles = await db.UserRoleAssignments.AsNoTracking()
            .Where(a => (a.CampusId == null || a.CampusId == slot.CampusId) && teachingRoles.Contains(a.Role!.Name))
            .Select(a => a.UserId).ToListAsync(ct);
        var fromAllocations = await db.TeacherAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.AcademicYearId == slot.AcademicYearId && a.CampusId == slot.CampusId)
            .Select(a => a.TeacherUserId).ToListAsync(ct);
        var pool = fromRoles.Concat(fromAllocations).Distinct().Where(id => id != absentTeacher).ToList();
        var active = await db.Users.AsNoTracking().Where(u => pool.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(ct);

        var absentToday = await db.Substitutions.AsNoTracking()
            .Where(s => s.Date == date && s.Status == SubstitutionStatus.Assigned && s.AbsentTeacherUserId != null)
            .Select(s => s.AbsentTeacherUserId!.Value).Distinct().ToListAsync(ct);
        var qualified = await db.TeacherAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.AcademicYearId == slot.AcademicYearId && a.SubjectId == slot.SubjectId)
            .Select(a => a.TeacherUserId).Distinct().ToListAsync(ct);

        var weekStart = date.AddDays(-(((int)date.DayOfWeek + 1) % 7)); // school week starts on Saturday
        var covers = await db.Substitutions.AsNoTracking()
            .Where(s => s.Date >= weekStart && s.Date < weekStart.AddDays(7) && s.Status == SubstitutionStatus.Assigned)
            .GroupBy(s => s.SubstituteUserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var dayLoad = await db.TimetableSlots.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.AcademicYearId == slot.AcademicYearId && s.Day == date.DayOfWeek && s.TeacherUserId != null)
            .GroupBy(s => s.TeacherUserId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var names = await lookups.UserNamesAsync(active.Select(a => (Guid?)a), ct);

        var result = new List<CoverCandidate>();
        foreach (var id in active.Where(a => !absentToday.Contains(a)))
        {
            if (!includeBusy && await IsBusyAsync(id, slot.AcademicYearId, date, view, ct)) continue;
            result.Add(new CoverCandidate(id, names.GetValueOrDefault(id, "?"), qualified.Contains(id), covers.GetValueOrDefault(id), dayLoad.GetValueOrDefault(id)));
        }
        return result.OrderByDescending(c => c.TeachesSubject).ThenBy(c => c.CoversThisWeek).ThenBy(c => c.PeriodsThatDay).ThenBy(c => c.Name).Take(includeBusy ? int.MaxValue : 12).ToList();
    }

    /// <summary>Busy = teaching (and not being covered) or already covering at an overlapping time on that date.</summary>
    private async Task<bool> IsBusyAsync(Guid userId, Guid yearId, DateOnly date, SlotView at, CancellationToken ct)
    {
        var own = await db.TimetableSlots.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.AcademicYearId == yearId && s.TeacherUserId == userId && s.Day == date.DayOfWeek).ToListAsync(ct);
        var coveredForThem = await db.Substitutions.AsNoTracking()
            .Where(s => s.Date == date && s.Status == SubstitutionStatus.Assigned && s.AbsentTeacherUserId == userId).Select(s => s.TimetableSlotId).ToListAsync(ct);
        var coverIds = await db.Substitutions.AsNoTracking()
            .Where(s => s.Date == date && s.Status == SubstitutionStatus.Assigned && s.SubstituteUserId == userId).Select(s => s.TimetableSlotId).ToListAsync(ct);
        var coverSlots = await db.TimetableSlots.IgnoreQueryFilters().AsNoTracking().Where(s => coverIds.Contains(s.Id)).ToListAsync(ct);

        var busy = await timetable.GetViewsAsync(own.Where(s => !coveredForThem.Contains(s.Id)).Concat(coverSlots).ToList(), ct);
        return busy.Any(b => AcademicLookups.Overlaps(b.StartTime, b.EndTime, at.StartTime, at.EndTime));
    }

    private async Task NotifyAsync(Substitution sub, SlotView view, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == sub.SubstituteUserId, ct);
        var data = new Dictionary<string, string?>
        {
            ["Date"] = sub.Date.ToString("dd MMM yyyy"), ["PeriodName"] = view.PeriodName, ["Time"] = $"{view.StartTime:HH\\:mm}–{view.EndTime:HH\\:mm}",
            ["SubjectName"] = view.SubjectName, ["ClassName"] = view.SectionLabel, ["Room"] = view.RoomName ?? "the usual room", ["Reason"] = sub.Reason
        };
        await notifications.QueueAsync(new NotificationRequest
        {
            TemplateCode = TemplateCodes.CoverAssigned, Channel = NotificationChannel.InApp, Recipient = user.Id.ToString(), RecipientUserId = user.Id,
            Data = data, CampusId = sub.CampusId, RelatedEntityType = nameof(Substitution), RelatedEntityId = sub.Id.ToString()
        }, ct);
        if (!string.IsNullOrWhiteSpace(user.Phone))
            await notifications.QueueAsync(new NotificationRequest
            {
                TemplateCode = TemplateCodes.CoverAssigned, Channel = NotificationChannel.Sms, Recipient = user.Phone, RecipientUserId = user.Id,
                Data = data, Language = user.PreferredLanguage, CampusId = sub.CampusId, RelatedEntityType = nameof(Substitution), RelatedEntityId = sub.Id.ToString()
            }, ct);
    }

    private async Task<List<SubstitutionView>> ViewsAsync(IReadOnlyList<Substitution> subs, CancellationToken ct)
    {
        var sections = await lookups.SectionsAsync(subs.Select(s => s.SectionId), ct);
        var subjects = await lookups.SubjectsAsync(ct);
        var names = await lookups.UserNamesAsync(subs.Select(s => (Guid?)s.SubstituteUserId).Concat(subs.Select(s => s.AbsentTeacherUserId)), ct);
        var result = new List<SubstitutionView>();
        foreach (var s in subs)
        {
            var info = sections.GetValueOrDefault(s.SectionId);
            var p = info is null ? null : (await lookups.PeriodsAsync(info.CampusId, info.ShiftId, ct)).FirstOrDefault(x => x.PeriodNo == s.PeriodNo);
            result.Add(new SubstitutionView(s.Id, s.Date, p?.Name ?? $"Period {s.PeriodNo}", p?.StartTime ?? default, p?.EndTime ?? default, info?.Label ?? "?",
                subjects.GetValueOrDefault(s.SubjectId).Name ?? "?", s.AbsentTeacherUserId, s.AbsentTeacherUserId is { } a ? names.GetValueOrDefault(a) : null,
                s.SubstituteUserId, names.GetValueOrDefault(s.SubstituteUserId, "?"), s.Reason, s.Status));
        }
        return result;
    }
}
