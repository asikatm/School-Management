using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Domain.Attendance;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Notifications;
using SchoolManagement.Infrastructure.Storage;

namespace SchoolManagement.Api.Modules.Attendance;

public record StudentLeaveView(Guid Id, Guid StudentId, string AdmissionNo, string StudentName, string? ClassName, Guid CampusId, DateOnly FromDate, DateOnly ToDate,
    int SchoolDays, string Reason, bool HasAttachment, string? AttachmentName, LeaveStatus Status, string? RequestedBy, DateTime RequestedAtUtc,
    string? DecidedBy, DateTime? DecidedAtUtc, string? DecisionNote, bool CanDecide);

public record LeaveDecisionRequest(bool Approve, string? Note);

public sealed class StudentLeaveForm
{
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public required string Reason { get; init; }
    public IFormFile? Attachment { get; init; }
}

/// <summary>
/// ATT-04: leave applications for students, from the parent portal or the school office, with a supporting document.
/// The class teacher (or anyone holding attendance.leave) decides; approval marks the school days as Leave.
/// </summary>
public sealed class StudentLeaveService(
    AppDbContext db,
    AcademicLookups lookups,
    SchoolDays schoolDays,
    GuardianNotifier guardians,
    INotificationService notifications,
    IFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const int MaxDays = 60;

    /// <summary>Applies for leave. Guardians may apply only for their own children; staff need attendance.take on the campus.</summary>
    public async Task<StudentLeaveView> ApplyAsync(Guid studentId, StudentLeaveForm form, bool asGuardian, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct) ?? throw new NotFoundException("Student", studentId);
        if (asGuardian) await EnsureGuardianOfAsync(studentId, ct);
        else currentUser.EnsurePermission(Permissions.Attendance.Take, student.CampusId);

        var today = lookups.TodayDhaka;
        if (form.ToDate < form.FromDate) throw new DomainException("The leave must end on or after its first day.", "bad_range");
        if (form.ToDate.DayNumber - form.FromDate.DayNumber + 1 > MaxDays) throw new DomainException($"Leave can cover at most {MaxDays} days per application.", "too_long");
        if (form.FromDate < today.AddDays(-7)) throw new DomainException("Leave can be applied for at most 7 days after the absence.", "too_late");
        if (string.IsNullOrWhiteSpace(form.Reason)) throw new DomainException("Give a reason for the leave.", "reason_required");
        if (await db.StudentLeaveRequests.AnyAsync(l => l.StudentId == studentId && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved)
                                                        && l.FromDate <= form.ToDate && l.ToDate >= form.FromDate, ct))
            throw new DomainException("There is already a leave application for some of these days.", "leave_overlaps", isConflict: true);

        var leave = new StudentLeaveRequest
        {
            CampusId = student.CampusId, StudentId = studentId, FromDate = form.FromDate, ToDate = form.ToDate, Reason = form.Reason.Trim(),
            Status = LeaveStatus.Pending, RequestedByUserId = currentUser.UserId
        };
        if (form.Attachment is { Length: > 0 } file)
        {
            await using var stream = file.OpenReadStream();
            var stored = await storage.SaveAsync(stream, $"students/{studentId:N}/leave", file.FileName, file.ContentType, ct);
            leave.AttachmentPath = stored.Path;
            leave.AttachmentName = stored.FileName;
            leave.AttachmentType = stored.ContentType;
        }
        db.StudentLeaveRequests.Add(leave);

        // The class teacher hears about it straight away.
        if (await schoolDays.SectionOfAsync(studentId, form.FromDate, ct) is { } sectionId
            && (await lookups.SectionsAsync([sectionId], ct)).GetValueOrDefault(sectionId)?.ClassTeacherUserId is { } teacher)
            notifications.QueueRaw(NotificationChannel.InApp, teacher.ToString(),
                $"Leave application for {student.FullName} ({form.FromDate:dd MMM} – {form.ToDate:dd MMM yyyy}): {leave.Reason}", "Student leave application",
                campusId: student.CampusId, recipientUserId: teacher);

        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([leave], ct))[0];
    }

    public async Task<List<StudentLeaveView>> ListAsync(LeaveStatus? status, Guid? campusId, Guid? sectionId, Guid? studentId, CancellationToken ct)
    {
        var q = db.StudentLeaveRequests.AsNoTracking();
        if (status is not null) q = q.Where(l => l.Status == status);
        if (campusId is not null) q = q.Where(l => l.CampusId == campusId);
        if (studentId is not null) q = q.Where(l => l.StudentId == studentId);
        if (sectionId is not null)
        {
            var inSection = db.StudentEnrolments.Where(e => e.SectionId == sectionId).Select(e => e.StudentId);
            q = q.Where(l => inSection.Contains(l.StudentId));
        }
        var rows = await q.OrderBy(l => l.Status).ThenByDescending(l => l.FromDate).Take(500).ToListAsync(ct);
        return await ViewsAsync(rows, ct);
    }

    /// <summary>Applications the parent made or that concern their children.</summary>
    public async Task<List<StudentLeaveView>> ForGuardianAsync(CancellationToken ct)
    {
        var children = await GuardianChildrenAsync(ct);
        var rows = await db.StudentLeaveRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(l => !l.IsDeleted && children.Contains(l.StudentId)).OrderByDescending(l => l.FromDate).Take(200).ToListAsync(ct);
        return await ViewsAsync(rows, ct);
    }

    public async Task<StudentLeaveView> DecideAsync(Guid id, LeaveDecisionRequest r, CancellationToken ct)
    {
        var leave = await db.StudentLeaveRequests.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Leave application", id);
        if (!await CanDecideAsync(leave, ct)) throw new ForbiddenException("Only the class teacher or an attendance approver can decide this application.");
        if (leave.Status != LeaveStatus.Pending) throw new DomainException($"This application is already {leave.Status.ToString().ToLowerInvariant()}.", "leave_decided", isConflict: true);
        if (!r.Approve && string.IsNullOrWhiteSpace(r.Note)) throw new DomainException("Give the guardian a reason for refusing the leave.", "note_required");

        leave.Status = r.Approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
        leave.DecidedByUserId = currentUser.UserId;
        leave.DecidedAtUtc = clock.GetUtcNow().UtcDateTime;
        leave.DecisionNote = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        if (r.Approve) await MarkLeaveDaysAsync(leave, ct);

        var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == leave.StudentId, ct);
        await guardians.NotifyAsync(leave.StudentId, leave.CampusId, TemplateCodes.StudentLeaveDecided, new Dictionary<string, string?>
        {
            ["ChildName"] = student.FullName, ["From"] = leave.FromDate.ToString("dd MMM"), ["To"] = leave.ToDate.ToString("dd MMM yyyy"),
            ["Decision"] = r.Approve ? "approved" : "not approved", ["DecisionBn"] = r.Approve ? "অনুমোদিত হয়েছে" : "অনুমোদিত হয়নি", ["Note"] = leave.DecisionNote
        }, nameof(StudentLeaveRequest), leave.Id.ToString(), ct);

        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([leave], ct))[0];
    }

    /// <summary>Withdraws an application: by its applicant while pending, or before the leave starts once approved (the Leave marks are removed).</summary>
    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var leave = await db.StudentLeaveRequests.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted, ct) ?? throw new NotFoundException("Leave application", id);
        var mine = leave.RequestedByUserId is not null && leave.RequestedByUserId == currentUser.UserId;
        if (!mine && !await CanDecideAsync(leave, ct)) throw new ForbiddenException("You cannot cancel this application.");
        if (leave.Status == LeaveStatus.Approved && leave.FromDate <= lookups.TodayDhaka)
            throw new DomainException("The leave has already started; ask the class teacher to correct the register instead.", "leave_started", isConflict: true);
        if (leave.Status is LeaveStatus.Rejected or LeaveStatus.Cancelled) throw new DomainException("This application is already closed.", "leave_closed", isConflict: true);

        if (leave.Status == LeaveStatus.Approved)
            db.StudentAttendances.RemoveRange(await db.StudentAttendances.IgnoreQueryFilters()
                .Where(a => a.StudentLeaveRequestId == leave.Id && a.Source == AttendanceSource.Leave).ToListAsync(ct));
        leave.Status = LeaveStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }

    public async Task<(Stream Content, string ContentType, string FileName)> AttachmentAsync(Guid id, bool asGuardian, CancellationToken ct)
    {
        var leave = await db.StudentLeaveRequests.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted, ct)
                    ?? throw new NotFoundException("Leave application", id);
        if (asGuardian) await EnsureGuardianOfAsync(leave.StudentId, ct);
        else currentUser.EnsurePermission(Permissions.Attendance.View, leave.CampusId);
        if (leave.AttachmentPath is null) throw new NotFoundException("Attachment", id);
        return (storage.OpenRead(leave.AttachmentPath), leave.AttachmentType ?? "application/octet-stream", leave.AttachmentName ?? "attachment");
    }

    public async Task EnsureGuardianOfAsync(Guid studentId, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Portal.Parent);
        if (!(await GuardianChildrenAsync(ct)).Contains(studentId)) throw new ForbiddenException("This student is not linked to your account.");
    }

    public async Task<List<Guid>> GuardianChildrenAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } me) return [];
        return await db.StudentGuardians.AsNoTracking().Where(g => g.Guardian!.UserId == me && !g.Guardian.IsDeleted).Select(g => g.StudentId).Distinct().ToListAsync(ct);
    }

    // ------------------------------------------------------------------ internals

    /// <summary>Approved leave marks each school day as Leave; days the teacher already marked present or late are left alone.</summary>
    private async Task MarkLeaveDaysAsync(StudentLeaveRequest leave, CancellationToken ct)
    {
        var days = await schoolDays.BetweenAsync(leave.CampusId, leave.FromDate, leave.ToDate, ct);
        var existing = await db.StudentAttendances.Where(a => a.StudentId == leave.StudentId && a.Date >= leave.FromDate && a.Date <= leave.ToDate).ToDictionaryAsync(a => a.Date, ct);
        foreach (var day in days)
        {
            if (existing.TryGetValue(day, out var row))
            {
                if (row.Mark is AttendanceMark.Present or AttendanceMark.Late) continue;
            }
            else
            {
                if (await schoolDays.SectionOfAsync(leave.StudentId, day, ct) is not { } sectionId) continue;
                var yearId = await db.Sections.IgnoreQueryFilters().Where(s => s.Id == sectionId).Select(s => s.AcademicYearId).FirstAsync(ct);
                row = new StudentAttendance { CampusId = leave.CampusId, AcademicYearId = yearId, StudentId = leave.StudentId, SectionId = sectionId, Date = day };
                db.StudentAttendances.Add(row);
            }
            row.Mark = AttendanceMark.Leave;
            row.Source = AttendanceSource.Leave;
            row.StudentLeaveRequestId = leave.Id;
            row.AlertSentAtUtc ??= clock.GetUtcNow().UtcDateTime; // no "absent" SMS for an approved leave
        }
    }

    private async Task<bool> CanDecideAsync(StudentLeaveRequest leave, CancellationToken ct)
    {
        if (currentUser.HasPermission(Permissions.Attendance.ApproveLeave, leave.CampusId)) return true;
        if (currentUser.UserId is not { } me) return false;
        var sectionId = await schoolDays.SectionOfAsync(leave.StudentId, leave.FromDate, ct) ?? await schoolDays.SectionOfAsync(leave.StudentId, lookups.TodayDhaka, ct);
        return sectionId is { } sid && await db.Sections.IgnoreQueryFilters().AnyAsync(s => s.Id == sid && s.ClassTeacherUserId == me, ct);
    }

    private async Task<List<StudentLeaveView>> ViewsAsync(IReadOnlyList<StudentLeaveRequest> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.StudentId).Distinct().ToList();
        var students = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.AdmissionNo, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName) }).ToDictionaryAsync(s => s.Id, ct);
        var names = await lookups.UserNamesAsync(rows.Select(r => r.RequestedByUserId).Concat(rows.Select(r => r.DecidedByUserId)), ct);
        var result = new List<StudentLeaveView>();
        foreach (var r in rows)
        {
            var sectionId = await schoolDays.SectionOfAsync(r.StudentId, r.FromDate, ct) ?? await schoolDays.SectionOfAsync(r.StudentId, lookups.TodayDhaka, ct);
            var label = sectionId is { } sid ? (await lookups.SectionsAsync([sid], ct)).GetValueOrDefault(sid)?.Label : null;
            var s = students.GetValueOrDefault(r.StudentId);
            var days = (await schoolDays.BetweenAsync(r.CampusId, r.FromDate, r.ToDate, ct)).Count;
            result.Add(new StudentLeaveView(r.Id, r.StudentId, s?.AdmissionNo ?? "?", s?.Name ?? "?", label, r.CampusId, r.FromDate, r.ToDate, days, r.Reason,
                r.AttachmentPath is not null, r.AttachmentName, r.Status, r.RequestedByUserId is { } by ? names.GetValueOrDefault(by) : null, r.CreatedAtUtc,
                r.DecidedByUserId is { } d ? names.GetValueOrDefault(d) : null, r.DecidedAtUtc, r.DecisionNote,
                r.Status == LeaveStatus.Pending && await CanDecideAsync(r, ct)));
        }
        return result;
    }
}
