using MudBlazor;
using SchoolManagement.Domain.Attendance;

namespace SchoolManagement.Web.Pages.Attendance;

/// <summary>Shared labels and colours for attendance marks and statuses.</summary>
public static class AttendanceUi
{
    public static readonly AttendanceMark[] Marks = [AttendanceMark.Present, AttendanceMark.Absent, AttendanceMark.Late, AttendanceMark.Leave];

    public static string Short(AttendanceMark m) => m switch
    {
        AttendanceMark.Present => "P",
        AttendanceMark.Absent => "A",
        AttendanceMark.Late => "L",
        _ => "LV"
    };

    public static Color Color(AttendanceMark? m) => m switch
    {
        AttendanceMark.Present => MudBlazor.Color.Success,
        AttendanceMark.Absent => MudBlazor.Color.Error,
        AttendanceMark.Late => MudBlazor.Color.Warning,
        AttendanceMark.Leave => MudBlazor.Color.Info,
        _ => MudBlazor.Color.Default
    };

    /// <summary>Cell colour for a month-grid code (P, A, L, LV, "-" closed, "" not marked).</summary>
    public static string CodeStyle(string code) => code switch
    {
        "P" => "color:#15803d",
        "A" => "color:#fff;background:#dc2626;font-weight:700",
        "L" => "color:#92400e;background:#fef3c7;font-weight:600",
        "LV" => "color:#1d4ed8;background:#dbeafe",
        "-" => "color:#94a3b8;background:rgba(148,163,184,.12)",
        _ => ""
    };

    public static Color Color(LeaveStatus s) => s switch
    {
        LeaveStatus.Approved => MudBlazor.Color.Success,
        LeaveStatus.Rejected => MudBlazor.Color.Error,
        LeaveStatus.Pending => MudBlazor.Color.Warning,
        _ => MudBlazor.Color.Default
    };

    public static Color Color(StaffAttendanceStatus? s) => s switch
    {
        StaffAttendanceStatus.Present => MudBlazor.Color.Success,
        StaffAttendanceStatus.Absent => MudBlazor.Color.Error,
        StaffAttendanceStatus.Leave => MudBlazor.Color.Info,
        _ => MudBlazor.Color.Default
    };

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6));

    public static string Minutes(int m) => m == 0 ? "—" : m < 60 ? $"{m} min" : $"{m / 60} h {m % 60:00} min";
}
