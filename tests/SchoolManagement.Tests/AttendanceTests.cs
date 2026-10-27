using SchoolManagement.Api.Modules.Attendance;
using SchoolManagement.Domain.Attendance;

namespace SchoolManagement.Tests;

public class AttendanceTests
{
    private static readonly StaffWorkRules Rules = new(new TimeOnly(7, 30), new TimeOnly(14, 30), 10, 30, null);

    // Dhaka is UTC+6: 07:30 local = 01:30 UTC.
    private static DateTime Utc(int hour, int minute) => new(2026, 11, 1, hour, minute, 0, DateTimeKind.Utc);

    private static StaffAttendance Day(DateTime? inUtc, DateTime? outUtc) =>
        new() { UserId = Guid.NewGuid(), Date = new DateOnly(2026, 11, 1), Status = StaffAttendanceStatus.Present, CheckInUtc = inUtc, CheckOutUtc = outUtc };

    [Fact]
    public void Arrival_within_the_grace_period_is_not_late()
    {
        var row = Day(Utc(1, 39), null); // 07:39 local
        StaffAttendanceService.Recalculate(row, Rules);
        Assert.Equal(0, row.LateMinutes);
    }

    [Fact]
    public void Lateness_past_the_grace_period_counts_from_the_start_time()
    {
        var row = Day(Utc(1, 52), null); // 07:52 local
        StaffAttendanceService.Recalculate(row, Rules);
        Assert.Equal(22, row.LateMinutes);
    }

    [Fact]
    public void Leaving_before_the_finish_time_is_early_leave()
    {
        var row = Day(Utc(1, 25), Utc(7, 45)); // out 13:45 local
        StaffAttendanceService.Recalculate(row, Rules);
        Assert.Equal(45, row.EarlyLeaveMinutes);
        Assert.Equal(0, row.OvertimeMinutes);
    }

    [Theory]
    [InlineData(8, 55, 0)]   // 14:55 — inside the 30-minute threshold
    [InlineData(9, 45, 75)]  // 15:45 — all 75 minutes count once past the threshold
    public void Overtime_starts_after_the_threshold(int hour, int minute, int expected)
    {
        var row = Day(Utc(1, 25), Utc(hour, minute));
        StaffAttendanceService.Recalculate(row, Rules);
        Assert.Equal(expected, row.OvertimeMinutes);
    }

    [Fact]
    public void Leave_days_carry_no_lateness_or_overtime()
    {
        var row = Day(Utc(3, 0), Utc(10, 0));
        row.Status = StaffAttendanceStatus.Leave;
        StaffAttendanceService.Recalculate(row, Rules);
        Assert.Equal(0, row.LateMinutes + row.EarlyLeaveMinutes + row.OvertimeMinutes);
    }

    [Fact]
    public void Geofence_setting_parses_latitude_longitude_and_radius()
    {
        Assert.Equal((23.7465, 90.3760, 150.0), StaffAttendanceService.ParseGeofence("23.7465, 90.3760, 150"));
        Assert.Null(StaffAttendanceService.ParseGeofence(""));
        Assert.Null(StaffAttendanceService.ParseGeofence("23.7,90.3"));
        Assert.Null(StaffAttendanceService.ParseGeofence("23.7,90.3,0"));
    }

    [Fact]
    public void Haversine_distance_is_accurate_at_campus_scale()
    {
        // 0.001° of latitude ≈ 111 m anywhere on Earth.
        var metres = StaffAttendanceService.DistanceMetres(23.7465, 90.3760, 23.7475, 90.3760);
        Assert.InRange(metres, 110, 112);
        Assert.Equal(0, StaffAttendanceService.DistanceMetres(23.7, 90.3, 23.7, 90.3), 3);
    }

    [Theory]
    [InlineData(17, 20, 85.0)]
    [InlineData(2, 3, 66.7)]
    [InlineData(0, 0, 100.0)] // nothing recorded yet is not a shortfall
    public void Attendance_percent_is_rounded_to_one_decimal(int attended, int marked, double expected) =>
        Assert.Equal((decimal)expected, SchoolDays.Percent(attended, marked));

    [Theory]
    [InlineData(AttendanceMark.Present, "P")]
    [InlineData(AttendanceMark.Absent, "A")]
    [InlineData(AttendanceMark.Late, "L")]
    [InlineData(AttendanceMark.Leave, "LV")]
    [InlineData(null, "")]
    public void Register_codes(AttendanceMark? mark, string code) => Assert.Equal(code, SchoolDays.Code(mark));
}
