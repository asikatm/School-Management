using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Domain.Attendance;

namespace SchoolManagement.Api.Modules.Attendance;

/// <summary>ATT-06: staff punches, the staff register and the monthly summary.</summary>
[ApiController]
[Route("api/staff-attendance")]
[Tags("Attendance: Staff")]
[Authorize]
public sealed class StaffAttendanceController(StaffAttendanceService staff, AcademicLookups lookups, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Mobile punch for the signed-in staff member: first punch checks in, later punches check out.</summary>
    [HttpPost("punch")]
    public Task<StaffDayView> Punch(PunchRequest request, CancellationToken ct) => staff.PunchAsync(request, ct);

    /// <summary>The signed-in staff member's day.</summary>
    [HttpGet("me")]
    public async Task<StaffDayView?> Me([FromQuery] Guid? campusId, CancellationToken ct)
    {
        var me = currentUser.UserId ?? throw new ForbiddenException("Sign in first.");
        var campus = campusId ?? await staff.HomeCampusAsync(me, ct);
        if (campus is null) return null;
        return (await staff.DayAsync(campus.Value, lookups.TodayDhaka, me, ct)).FirstOrDefault();
    }

    /// <summary>Punches from a biometric / RFID device, matched by user name.</summary>
    [HttpPost("device")]
    [HasPermission(Permissions.Attendance.Staff)]
    public Task<DevicePunchResult> Device([FromQuery] Guid campusId, IReadOnlyList<DevicePunch> punches, CancellationToken ct) => staff.ImportAsync(campusId, punches, ct);

    [HttpGet("day")]
    [HasPermission(Permissions.Attendance.Staff)]
    public Task<List<StaffDayView>> Day([FromQuery] Guid campusId, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Attendance.Staff, campusId);
        return staff.DayAsync(campusId, date ?? lookups.TodayDhaka, null, ct);
    }

    [HttpPut("day")]
    [HasPermission(Permissions.Attendance.Staff)]
    public Task<StaffDayView> Correct(StaffCorrection request, CancellationToken ct) => staff.CorrectAsync(request, ct);

    [HttpGet("monthly")]
    [HasPermission(Permissions.Attendance.Staff)]
    public Task<List<StaffMonthlyRow>> Monthly([FromQuery] Guid campusId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Attendance.Staff, campusId);
        var today = lookups.TodayDhaka;
        return staff.MonthlyAsync(campusId, year ?? today.Year, month ?? today.Month, ct);
    }
}

/// <summary>ATT-07: leave types, entitlements, balances and staff leave requests (decided in the approval inbox).</summary>
[ApiController]
[Route("api/staff-leave")]
[Tags("Attendance: Staff leave")]
[Authorize]
public sealed class StaffLeaveController(StaffLeaveService leaves, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("types")]
    public Task<List<LeaveTypeView>> Types([FromQuery] bool activeOnly = true, CancellationToken ct = default) => leaves.TypesAsync(activeOnly, ct);

    [HttpPost("types")]
    [HasPermission(Permissions.StaffLeave.Manage)]
    public Task<LeaveTypeView> CreateType(LeaveTypeRequest request, CancellationToken ct) => leaves.SaveTypeAsync(null, request, ct);

    [HttpPut("types/{id:guid}")]
    [HasPermission(Permissions.StaffLeave.Manage)]
    public Task<LeaveTypeView> UpdateType(Guid id, LeaveTypeRequest request, CancellationToken ct) => leaves.SaveTypeAsync(id, request, ct);

    /// <summary>Overrides a person's entitlement for a year (e.g. pro-rata for a mid-year joiner).</summary>
    [HttpPut("entitlements")]
    [HasPermission(Permissions.StaffLeave.Manage)]
    public async Task<IActionResult> Entitlement(EntitlementRequest request, CancellationToken ct)
    {
        await leaves.SetEntitlementAsync(request, ct);
        return NoContent();
    }

    [HttpGet("balances")]
    public Task<List<LeaveBalance>> Balances([FromQuery] Guid? userId, [FromQuery] Guid? academicYearId, CancellationToken ct) =>
        leaves.BalancesAsync(userId ?? currentUser.UserId ?? throw new ForbiddenException("Sign in first."), academicYearId, ct);

    [HttpGet("mine")]
    public Task<List<StaffLeaveView>> Mine(CancellationToken ct) => leaves.MineAsync(ct);

    [HttpPost]
    public Task<StaffLeaveView> Apply(StaffLeaveApply request, CancellationToken ct) => leaves.ApplyAsync(request, ct);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await leaves.CancelAsync(id, ct);
        return NoContent();
    }

    /// <summary>All requests in a range — the leave calendar.</summary>
    [HttpGet]
    [HasPermission(Permissions.StaffLeave.Manage)]
    public Task<List<StaffLeaveView>> List([FromQuery] LeaveStatus? status, [FromQuery] Guid? campusId, [FromQuery] Guid? userId, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, CancellationToken ct) => leaves.ListAsync(status, campusId, userId, from, to, ct);
}

public sealed class StaffLeaveApplyValidator : AbstractValidator<StaffLeaveApply>
{
    public StaffLeaveApplyValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class LeaveTypeRequestValidator : AbstractValidator<LeaveTypeRequest>
{
    public LeaveTypeRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DaysPerYear).InclusiveBetween(0, 365);
    }
}

public sealed class StaffCorrectionValidator : AbstractValidator<StaffCorrection>
{
    public StaffCorrectionValidator() => RuleFor(x => x.Note).MaximumLength(300);
}
