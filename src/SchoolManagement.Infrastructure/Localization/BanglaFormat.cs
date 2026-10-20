using System.Globalization;
using System.Text;

namespace SchoolManagement.Infrastructure.Localization;

/// <summary>CORE-06 / spec 5.10 localisation helpers: Bangla digits, lakh/crore grouping, Hijri dates, Asia/Dhaka time.</summary>
public static class BanglaFormat
{
    public static readonly TimeZoneInfo Dhaka = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    private static readonly char[] BanglaDigits = ['০', '১', '২', '৩', '৪', '৫', '৬', '৭', '৮', '৯'];

    public static string ToBanglaDigits(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(c is >= '0' and <= '9' ? BanglaDigits[c - '0'] : c);
        return sb.ToString();
    }

    /// <summary>Formats with South-Asian grouping: 6,60,000.50 (last three digits, then pairs).</summary>
    public static string GroupLakhCrore(decimal amount, int decimals = 2)
    {
        var negative = amount < 0;
        var fixedText = Math.Abs(amount).ToString("F" + decimals, CultureInfo.InvariantCulture);
        var parts = fixedText.Split('.');
        var integer = parts[0];

        string grouped;
        if (integer.Length <= 3)
            grouped = integer;
        else
        {
            var last3 = integer[^3..];
            var rest = integer[..^3];
            var sb = new StringBuilder();
            for (var i = 0; i < rest.Length; i++)
            {
                if (i > 0 && (rest.Length - i) % 2 == 0) sb.Append(',');
                sb.Append(rest[i]);
            }
            grouped = sb + "," + last3;
        }

        var result = parts.Length > 1 ? grouped + "." + parts[1] : grouped;
        return negative ? "-" + result : result;
    }

    public static string FormatBdt(decimal amount, bool bangla = false)
    {
        var text = "৳" + GroupLakhCrore(amount);
        return bangla ? ToBanglaDigits(text) : text;
    }

    public static DateTime ToDhaka(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Dhaka);

    private static readonly string[] HijriMonths =
    [
        "Muharram", "Safar", "Rabi al-Awwal", "Rabi al-Thani", "Jumada al-Ula", "Jumada al-Akhirah",
        "Rajab", "Sha'ban", "Ramadan", "Shawwal", "Dhu al-Qadah", "Dhu al-Hijjah"
    ];

    /// <summary>
    /// Tabular (Umm al-Qura) Hijri date with an optional day adjustment for local moon-sighting (CCA-07).
    /// </summary>
    public static string ToHijri(DateOnly date, int adjustDays = 0)
    {
        var cal = new UmAlQuraCalendar();
        var dt = date.ToDateTime(TimeOnly.MinValue).AddDays(adjustDays);
        return $"{cal.GetDayOfMonth(dt)} {HijriMonths[cal.GetMonth(dt) - 1]} {cal.GetYear(dt)} AH";
    }
}
