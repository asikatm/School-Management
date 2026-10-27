using System.Globalization;
using MudBlazor;
using SchoolManagement.Domain.Exams;

namespace SchoolManagement.Web.Pages.Exams;

/// <summary>Shared labels and colours for the EXM pages.</summary>
public static class ExamUi
{
    public static Color Color(MarkSheetStatus s) => s switch
    {
        MarkSheetStatus.Approved => MudBlazor.Color.Success,
        MarkSheetStatus.Verified => MudBlazor.Color.Info,
        MarkSheetStatus.Submitted => MudBlazor.Color.Warning,
        _ => MudBlazor.Color.Default
    };

    public static Color Color(ResultStatus s) => s switch
    {
        ResultStatus.Published => MudBlazor.Color.Success,
        ResultStatus.Scheduled => MudBlazor.Color.Info,
        ResultStatus.Finalised => MudBlazor.Color.Warning,
        _ => MudBlazor.Color.Default
    };

    public static string Label(ResultStatus s) => s switch
    {
        ResultStatus.NotReady => "Marks in progress",
        ResultStatus.Finalised => "Results finalised",
        ResultStatus.Scheduled => "Publication scheduled",
        _ => "Results published"
    };

    public static string Time(TimeOnly t) => t.ToString("hh:mm tt", CultureInfo.InvariantCulture);

    /// <summary>"AB", "EX" or the number typed in a mark cell.</summary>
    public static string Cell(decimal? marks, MarkCode? code) => code switch
    {
        MarkCode.Absent => "AB",
        MarkCode.Exempt => "EX",
        _ => marks?.ToString("0.##", CultureInfo.InvariantCulture) ?? ""
    };

    public static bool TryParseCell(string? text, out decimal? marks, out MarkCode code)
    {
        marks = null;
        code = MarkCode.None;
        var t = text?.Trim() ?? "";
        if (t.Length == 0) return true;
        if (t.Equals("AB", StringComparison.OrdinalIgnoreCase)) { code = MarkCode.Absent; return true; }
        if (t.Equals("EX", StringComparison.OrdinalIgnoreCase)) { code = MarkCode.Exempt; return true; }
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var m)) return false;
        marks = m;
        return true;
    }

    /// <summary>Heat-map cell colour from red (low) through amber to green (high).</summary>
    public static string Heat(decimal pct) => pct switch
    {
        >= 80 => "background:#dcfce7;color:#14532d",
        >= 65 => "background:#ecfccb;color:#365314",
        >= 50 => "background:#fef9c3;color:#713f12",
        >= 40 => "background:#ffedd5;color:#7c2d12",
        _ => "background:#fee2e2;color:#7f1d1d"
    };
}
