using System.Globalization;
using System.Text.RegularExpressions;

namespace SchoolManagement.Web.Shared;

/// <summary>Display helpers. The API sends UTC; the school runs on Asia/Dhaka (UTC+6, no DST).</summary>
public static partial class Format
{
    private static readonly TimeSpan Dhaka = TimeSpan.FromHours(6);

    public static string Date(DateOnly? d) => d?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "—";
    public static string Date(DateTime? utc) => utc is null ? "—" : (utc.Value + Dhaka).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public static string DateTime(DateTime? utc) => utc is null ? "—" : (utc.Value + Dhaka).ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture);
    public static string Money(decimal? amount) => amount is null ? "—" : "৳ " + amount.Value.ToString("#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>"AwaitingApproval" → "Awaiting approval".</summary>
    public static string Enum<T>(T value) where T : struct, System.Enum
    {
        var words = Words().Replace(value.ToString(), " $1").Trim();
        return words.Length == 0 ? words : words[0] + words[1..].ToLowerInvariant();
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex Words();
}
