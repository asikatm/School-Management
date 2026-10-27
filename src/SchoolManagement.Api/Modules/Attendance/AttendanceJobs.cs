using System.Net;
using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Domain.Attendance;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Jobs;
using SchoolManagement.Infrastructure.Localization;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Attendance;

/// <summary>
/// ATT-03: after the campus cut-off time (so late arrivals can be marked first), sends one SMS per absent or late
/// student to the guardians. Each mark is alerted once; leave approved in advance never triggers an alert.
/// </summary>
public sealed class AttendanceAlertJob : IScheduledJob
{
    public string Name => "attendance-alerts";
    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var settings = services.GetRequiredService<ISettingsService>();
        var lookups = services.GetRequiredService<AcademicLookups>();
        var guardians = services.GetRequiredService<GuardianNotifier>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var today = lookups.TodayDhaka;
        var timeNow = TimeOnly.FromDateTime(BanglaFormat.ToDhaka(now));

        var pending = await db.StudentAttendances
            .Where(a => a.Date == today && a.AlertSentAtUtc == null && (a.Mark == AttendanceMark.Absent || a.Mark == AttendanceMark.Late))
            .ToListAsync(ct);
        if (pending.Count == 0) return;

        var sections = await lookups.SectionsAsync(pending.Select(p => p.SectionId), ct);
        var ids = pending.Select(p => p.StudentId).Distinct().ToList();
        var names = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName) }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        foreach (var campus in pending.GroupBy(p => p.CampusId))
        {
            if (!await settings.GetBoolAsync(SettingKeys.AttendanceSendAlerts, true, campus.Key, ct))
            {
                foreach (var row in campus) row.AlertSentAtUtc = now; // switched off: don't send a backlog if it is turned on later
                continue;
            }
            if (timeNow < await settings.GetTimeAsync(SettingKeys.AttendanceAlertCutoff, new TimeOnly(9, 30), campus.Key, ct)) continue;

            foreach (var row in campus)
            {
                var late = row.Mark == AttendanceMark.Late;
                await guardians.NotifyAsync(row.StudentId, row.CampusId, late ? TemplateCodes.StudentLate : TemplateCodes.StudentAbsent, new Dictionary<string, string?>
                {
                    ["ChildName"] = names.GetValueOrDefault(row.StudentId, "Your child"), ["ClassName"] = sections.GetValueOrDefault(row.SectionId)?.Label,
                    ["Date"] = row.Date.ToString("dd MMM yyyy"), ["Arrival"] = late && row.ArrivalTime is { } t ? $" at {t:HH\\:mm}" : ""
                }, nameof(StudentAttendance), row.Id.ToString(), ct, late ? MessagePriority.Normal : MessagePriority.High);
                row.AlertSentAtUtc = now;
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>ATT-05: warns guardians once per level per term when term attendance drops below the warning line or the exam minimum.</summary>
public sealed class AttendanceWarningJob : IScheduledJob
{
    /// <summary>Too few marked days make the percentage meaningless (one absence in week one would be 80%).</summary>
    public const int MinimumMarkedDays = 10;

    public string Name => "attendance-warnings";
    public TimeSpan Interval => TimeSpan.FromHours(6);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var attendance = services.GetRequiredService<AttendanceService>();
        var guardians = services.GetRequiredService<GuardianNotifier>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var campuses = await db.Campuses.AsNoTracking().Where(c => c.IsActive).Select(c => c.Id).ToListAsync(ct);
        foreach (var campusId in campuses)
        {
            ThresholdReport report;
            try { report = await attendance.ThresholdsAsync(null, campusId, null, true, ct); }
            catch (DomainException) { continue; } // no term yet

            foreach (var row in report.Students.Where(r => r.Level is not null && r.Marked >= MinimumMarkedDays))
            {
                var level = row.Level!.Value;
                if (await db.AttendanceWarnings.AnyAsync(w => w.StudentId == row.StudentId && w.TermId == report.TermId && w.Level == level, ct)) continue;
                db.AttendanceWarnings.Add(new AttendanceWarning
                {
                    CampusId = row.CampusId, StudentId = row.StudentId, TermId = report.TermId, Level = level, Percent = row.Percent, SentAtUtc = now
                });
                await guardians.NotifyAsync(row.StudentId, row.CampusId, TemplateCodes.LowAttendance, new Dictionary<string, string?>
                {
                    ["ChildName"] = row.Name, ["ClassName"] = row.ClassName, ["Percent"] = row.Percent.ToString("0.#"), ["Term"] = report.TermName,
                    ["Minimum"] = report.MinimumPercent.ToString("0.#")
                }, nameof(AttendanceWarning), row.StudentId.ToString(), ct, MessagePriority.High);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}

/// <summary>ATT-05: the printable warning letter to guardians.</summary>
public sealed class WarningLetterBuilder(AppDbContext db, AttendanceService attendance, AcademicLookups lookups)
{
    private const string Css = """
        body{font-family:'Segoe UI',Arial,sans-serif;color:#1f2937;max-width:720px;margin:32px auto;padding:0 24px;line-height:1.55}
        .head{display:flex;justify-content:space-between;border-bottom:3px solid #0b3d6e;padding-bottom:10px;margin-bottom:24px}
        .school{font-size:22px;font-weight:700;color:#0b3d6e}.campus{color:#475569}
        table{border-collapse:collapse;margin:16px 0}td{padding:4px 14px 4px 0}td:first-child{color:#64748b}
        .pct{font-size:20px;font-weight:700;color:#b91c1c}.sign{margin-top:56px}
        @media print{body{margin:0}}
        """;

    public async Task<string> BuildAsync(Guid studentId, Guid? termId, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct) ?? throw new NotFoundException("Student", studentId);
        var report = await attendance.ThresholdsAsync(termId, student.CampusId, null, false, ct);
        var row = report.Students.FirstOrDefault(r => r.StudentId == studentId)
                  ?? throw new DomainException("No attendance has been recorded for this student this term.", "no_attendance");
        var campus = await db.Campuses.AsNoTracking().FirstAsync(c => c.Id == student.CampusId, ct);
        var org = await db.Organisations.AsNoTracking().Where(o => o.Id == campus.OrganisationId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "";
        var guardian = await db.StudentGuardians.AsNoTracking().Where(g => g.StudentId == studentId)
            .OrderByDescending(g => g.IsPrimaryContact).Select(g => g.Guardian!.Name).FirstOrDefaultAsync(ct);

        var below = row.Percent < report.MinimumPercent;
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        return $"""
            <!doctype html><html><head><meta charset="utf-8"><title>Attendance warning — {E(student.FullName)}</title><style>{Css}</style></head><body>
            <div class="head"><div><div class="school">{E(org)}</div><div class="campus">{E(campus.Name)}{(campus.Address is null ? "" : " · " + E(campus.Address))}</div></div>
            <div>{lookups.TodayDhaka:dd MMMM yyyy}</div></div>
            <p>Dear {E(guardian ?? "Parent / Guardian")},</p>
            <p><strong>Subject: {(below ? "Attendance below the examination minimum" : "Low attendance warning")} — {E(report.TermName)}</strong></p>
            <p>We are writing to let you know that the attendance of your child has fallen below the level the school expects.</p>
            <table>
            <tr><td>Student</td><td><strong>{E(student.FullName)}</strong> ({E(student.AdmissionNo)})</td></tr>
            <tr><td>Class</td><td>{E(row.ClassName)}</td></tr>
            <tr><td>Period</td><td>{report.From:dd MMM yyyy} – {report.To:dd MMM yyyy}</td></tr>
            <tr><td>School days recorded</td><td>{row.Marked}</td></tr>
            <tr><td>Days attended</td><td>{row.Attended}</td></tr>
            <tr><td>Absent / on leave</td><td>{row.Absent} / {row.Leave}</td></tr>
            <tr><td>Attendance</td><td class="pct">{row.Percent:0.#}%</td></tr>
            </table>
            <p>The school expects at least <strong>{report.WarningPercent:0.#}%</strong> attendance, and a student needs
            <strong>{report.MinimumPercent:0.#}%</strong> to be eligible to sit the term examinations.
            {(below ? "Your child is currently <strong>below the examination minimum</strong>." : "")}</p>
            <p>Please make sure your child attends regularly and contact the class teacher if there is a reason we should know about.</p>
            <div class="sign">______________________<br>Principal</div>
            </body></html>
            """;
    }
}
