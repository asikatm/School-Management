using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Core;

namespace SchoolManagement.Api.Modules.Academics;

public enum CalendarItemKind
{
    Term = 0,
    Holiday = 1,
    Event = 2
}

public record CalendarItem(Guid Id, CalendarItemKind Kind, string Title, string? TitleBn, string Category, DateOnly StartDate, DateOnly EndDate,
    TimeOnly? StartTime, TimeOnly? EndTime, Guid? CampusId, string? Location, string? Description, bool IsPublic, bool IsPublished, bool ForStaff, bool ForParents);

public record CalendarEventRequest(Guid AcademicYearId, Guid? CampusId, string Title, string? TitleBn, CalendarEventType Type, DateOnly StartDate, DateOnly EndDate,
    TimeOnly? StartTime, TimeOnly? EndTime, string? Location, string? Description, bool ForStaff, bool ForParents, bool IsPublic, bool IsPublished);

/// <summary>ACA-07: one calendar of terms, holidays (national, Islamic, school) and events — for staff, parents and the website.</summary>
[ApiController]
[Route("api/academics/calendar")]
[Tags("Academics: Calendar")]
public sealed class CalendarController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Everything between two dates. Unpublished events are shown only to calendar managers.</summary>
    [HttpGet]
    [Authorize]
    public Task<List<CalendarItem>> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? campusId, CancellationToken ct) =>
        CalendarQueries.ItemsAsync(db, from, to, campusId, includeDrafts: currentUser.HasPermission(Permissions.Academic.Calendar), publicOnly: false, ct);

    [HttpPost("events")]
    [HasPermission(Permissions.Academic.Calendar)]
    public async Task<CalendarItem> Create(CalendarEventRequest r, CancellationToken ct)
    {
        if (r.CampusId is { } c) currentUser.EnsurePermission(Permissions.Academic.Calendar, c);
        var e = new CalendarEvent { Title = r.Title, AcademicYearId = r.AcademicYearId };
        Apply(e, r);
        db.CalendarEvents.Add(e);
        await db.SaveChangesAsync(ct);
        return CalendarQueries.ToItem(e);
    }

    [HttpPut("events/{id:guid}")]
    [HasPermission(Permissions.Academic.Calendar)]
    public async Task<CalendarItem> Update(Guid id, CalendarEventRequest r, CancellationToken ct)
    {
        var e = await db.CalendarEvents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Event", id);
        if (e.CampusId is { } old) currentUser.EnsurePermission(Permissions.Academic.Calendar, old);
        if (r.CampusId is { } c) currentUser.EnsurePermission(Permissions.Academic.Calendar, c);
        Apply(e, r);
        await db.SaveChangesAsync(ct);
        return CalendarQueries.ToItem(e);
    }

    [HttpDelete("events/{id:guid}")]
    [HasPermission(Permissions.Academic.Calendar)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var e = await db.CalendarEvents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Event", id);
        if (e.CampusId is { } c) currentUser.EnsurePermission(Permissions.Academic.Calendar, c);
        db.CalendarEvents.Remove(e);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(CalendarEvent e, CalendarEventRequest r)
    {
        e.AcademicYearId = r.AcademicYearId;
        e.CampusId = r.CampusId;
        e.Title = r.Title.Trim();
        e.TitleBn = string.IsNullOrWhiteSpace(r.TitleBn) ? null : r.TitleBn.Trim();
        e.Type = r.Type;
        e.StartDate = r.StartDate;
        e.EndDate = r.EndDate;
        e.StartTime = r.StartTime;
        e.EndTime = r.EndTime;
        e.Location = r.Location;
        e.Description = r.Description;
        e.ForStaff = r.ForStaff;
        e.ForParents = r.ForParents;
        e.IsPublic = r.IsPublic;
        e.IsPublished = r.IsPublished;
    }
}

/// <summary>Calendar for the public website and calendar apps (WEB-01): published public events, terms and holidays only.</summary>
[ApiController]
[Route("api/public/calendar")]
[Tags("Public: Website")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.PublicForms)]
public sealed class PublicCalendarController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<List<CalendarItem>> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? campusId, CancellationToken ct)
    {
        using var _ = db.ElevateToSystem();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return await CalendarQueries.ItemsAsync(db, from ?? today.AddDays(-30), to ?? today.AddDays(365), campusId, includeDrafts: false, publicOnly: true, ct);
    }

    /// <summary>iCalendar feed: subscribe from Google Calendar, Outlook or a phone.</summary>
    [HttpGet("feed.ics")]
    public async Task<IActionResult> Ics([FromQuery] Guid? campusId, CancellationToken ct)
    {
        using var _ = db.ElevateToSystem();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var items = await CalendarQueries.ItemsAsync(db, today.AddDays(-60), today.AddDays(400), campusId, includeDrafts: false, publicOnly: true, ct);
        var school = await db.Organisations.Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "School";
        return File(Encoding.UTF8.GetBytes(CalendarQueries.ToIcs(items, school, clock.GetUtcNow().UtcDateTime)), "text/calendar; charset=utf-8", "school-calendar.ics");
    }
}

public static class CalendarQueries
{
    public static async Task<List<CalendarItem>> ItemsAsync(AppDbContext db, DateOnly from, DateOnly to, Guid? campusId, bool includeDrafts, bool publicOnly, CancellationToken ct)
    {
        if (to < from) throw new DomainException("'to' must be on or after 'from'.", "invalid_range");
        if (to.DayNumber - from.DayNumber > 800) throw new DomainException("Ask for at most about two years at a time.", "range_too_long");

        var items = new List<CalendarItem>();
        var terms = await db.Terms.AsNoTracking().Include(t => t.AcademicYear)
            .Where(t => t.StartDate <= to && t.EndDate >= from).ToListAsync(ct);
        items.AddRange(terms.Select(t => new CalendarItem(t.Id, CalendarItemKind.Term, $"{t.Name} ({t.AcademicYear?.Name})", t.NameBn, "Term",
            t.StartDate, t.EndDate, null, null, null, null, null, true, true, true, true)));

        var holidays = await db.Holidays.AsNoTracking()
            .Where(h => h.StartDate <= to && h.EndDate >= from && (campusId == null || h.CampusId == null || h.CampusId == campusId)).ToListAsync(ct);
        items.AddRange(holidays.Select(h => new CalendarItem(h.Id, CalendarItemKind.Holiday, h.Name, h.NameBn, h.Type switch
        {
            HolidayType.Islamic => "Islamic holiday",
            HolidayType.National => "National holiday",
            _ => "School holiday"
        }, h.StartDate, h.EndDate, null, null, h.CampusId, null, null, true, true, true, true)));

        var q = db.CalendarEvents.AsNoTracking().Where(e => e.StartDate <= to && e.EndDate >= from && (campusId == null || e.CampusId == null || e.CampusId == campusId));
        if (!includeDrafts) q = q.Where(e => e.IsPublished);
        if (publicOnly) q = q.Where(e => e.IsPublic);
        items.AddRange((await q.ToListAsync(ct)).Select(ToItem));

        return items.OrderBy(i => i.StartDate).ThenBy(i => i.Kind).ThenBy(i => i.StartTime).ToList();
    }

    public static CalendarItem ToItem(CalendarEvent e) => new(e.Id, CalendarItemKind.Event, e.Title, e.TitleBn, e.Type switch
    {
        CalendarEventType.ExamWindow => "Exam",
        CalendarEventType.ParentTeacherMeeting => "Parent–teacher meeting",
        CalendarEventType.Activity => "Activity",
        CalendarEventType.Meeting => "Meeting",
        CalendarEventType.Other => "Other",
        _ => "Event"
    }, e.StartDate, e.EndDate, e.StartTime, e.EndTime, e.CampusId, e.Location, e.Description, e.IsPublic, e.IsPublished, e.ForStaff, e.ForParents);

    /// <summary>RFC 5545 output. All-day items use DATE values with an exclusive end; timed items are in Asia/Dhaka (UTC+6, no DST).</summary>
    public static string ToIcs(IEnumerable<CalendarItem> items, string school, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(Fold(s)).Append("\r\n");
        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//EMSL//School Management//EN");
        Line("CALSCALE:GREGORIAN");
        Line($"X-WR-CALNAME:{Escape(school)}");
        Line("X-WR-TIMEZONE:Asia/Dhaka");
        foreach (var i in items)
        {
            Line("BEGIN:VEVENT");
            Line($"UID:{i.Id}-{i.Kind}@school-management");
            Line($"DTSTAMP:{nowUtc:yyyyMMdd'T'HHmmss'Z'}");
            if (i.StartTime is { } st && i.StartDate == i.EndDate)
            {
                var start = i.StartDate.ToDateTime(st).AddHours(-6);
                var end = i.StartDate.ToDateTime(i.EndTime ?? st.AddHours(1)).AddHours(-6);
                Line($"DTSTART:{start:yyyyMMdd'T'HHmmss'Z'}");
                Line($"DTEND:{end:yyyyMMdd'T'HHmmss'Z'}");
            }
            else
            {
                Line($"DTSTART;VALUE=DATE:{i.StartDate:yyyyMMdd}");
                Line($"DTEND;VALUE=DATE:{i.EndDate.AddDays(1):yyyyMMdd}");
            }
            Line($"SUMMARY:{Escape(i.Title)}");
            Line($"CATEGORIES:{Escape(i.Category)}");
            if (!string.IsNullOrWhiteSpace(i.Location)) Line($"LOCATION:{Escape(i.Location)}");
            if (!string.IsNullOrWhiteSpace(i.Description)) Line($"DESCRIPTION:{Escape(i.Description)}");
            Line("END:VEVENT");
        }
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    /// <summary>Lines longer than 75 octets are folded (continuation lines start with a space).</summary>
    private static string Fold(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        if (bytes.Length <= 75) return line;
        var sb = new StringBuilder();
        var current = 0;
        var limit = 75; // continuation lines carry a leading space, so they hold one octet less
        foreach (var rune in line.EnumerateRunes())
        {
            var len = rune.Utf8SequenceLength;
            if (current + len > limit)
            {
                sb.Append("\r\n ");
                current = 0;
                limit = 74;
            }
            sb.Append(rune.ToString());
            current += len;
        }
        return sb.ToString();
    }
}

public sealed class CalendarEventRequestValidator : AbstractValidator<CalendarEventRequest>
{
    public CalendarEventRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime).When(x => x.StartTime is not null && x.EndTime is not null);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}
