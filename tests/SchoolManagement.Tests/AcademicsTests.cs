using System.Text;
using SchoolManagement.Api.Modules.Academics;

namespace SchoolManagement.Tests;

public class AcademicsTests
{
    [Theory]
    [InlineData("08:00", "08:40", "08:40", "09:20", false)]
    [InlineData("08:00", "08:40", "08:30", "09:10", true)]
    [InlineData("08:00", "09:00", "08:15", "08:45", true)]
    [InlineData("10:00", "10:40", "08:00", "08:40", false)]
    public void Periods_overlap_by_clock_time(string a1, string a2, string b1, string b2, bool expected) =>
        Assert.Equal(expected, AcademicLookups.Overlaps(TimeOnly.Parse(a1), TimeOnly.Parse(a2), TimeOnly.Parse(b1), TimeOnly.Parse(b2)));

    private static CalendarItem Item(string title, DateOnly start, DateOnly end, TimeOnly? from = null, TimeOnly? to = null, string? description = null) =>
        new(Guid.NewGuid(), CalendarItemKind.Event, title, null, "Event", start, end, from, to, null, null, description, true, true, true, true);

    [Fact]
    public void Ics_all_day_events_use_an_exclusive_end_date()
    {
        var ics = CalendarQueries.ToIcs([Item("Victory Day", new(2026, 12, 16), new(2026, 12, 16))], "School", new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains("DTSTART;VALUE=DATE:20261216\r\n", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261217\r\n", ics);
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
    }

    [Fact]
    public void Ics_timed_events_are_converted_from_Dhaka_to_UTC()
    {
        var ics = CalendarQueries.ToIcs([Item("Staff meeting", new(2026, 11, 2), new(2026, 11, 2), new(14, 0), new(15, 30))], "School", DateTime.UtcNow);
        Assert.Contains("DTSTART:20261102T080000Z", ics);
        Assert.Contains("DTEND:20261102T093000Z", ics);
    }

    [Fact]
    public void Ics_escapes_text_and_folds_long_lines_at_75_octets()
    {
        var title = "Annual sports day, prize giving; parents welcome — বার্ষিক ক্রীড়া প্রতিযোগিতা ও পুরস্কার বিতরণী অনুষ্ঠান";
        var ics = CalendarQueries.ToIcs([Item(title, new(2026, 12, 10), new(2026, 12, 10), description: "Line one\nLine two")], "School", DateTime.UtcNow);

        foreach (var line in ics.Split("\r\n"))
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"Line too long: {line}");

        var unfolded = ics.Replace("\r\n ", "");
        Assert.Contains("SUMMARY:Annual sports day\\, prize giving\\; parents welcome — বার্ষিক", unfolded);
        Assert.Contains("DESCRIPTION:Line one\\nLine two", unfolded);
    }
}
