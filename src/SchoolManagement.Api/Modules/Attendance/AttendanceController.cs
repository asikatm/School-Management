using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Domain.Attendance;

namespace SchoolManagement.Api.Modules.Attendance;

/// <summary>ATT-01/02/05: student registers, the daily overview, monthly register and attendance thresholds.</summary>
[ApiController]
[Route("api/attendance")]
[Tags("Attendance: Students")]
public sealed class AttendanceController(AttendanceService attendance, WarningLetterBuilder letters, AcademicLookups lookups) : ControllerBase
{
    /// <summary>A section's register for a day (daily, or one period when <c>periodNo</c> is given for period-wise classes).</summary>
    [HttpGet("register")]
    [HasPermission(Permissions.Attendance.View)]
    public Task<RegisterView> Register([FromQuery] Guid sectionId, [FromQuery] DateOnly? date, [FromQuery] int? periodNo, CancellationToken ct) =>
        attendance.GetAsync(sectionId, date ?? lookups.TodayDhaka, periodNo, ct);

    [HttpPost("register")]
    [HasPermission(Permissions.Attendance.Take)]
    public Task<RegisterView> Save(SaveRegisterRequest request, CancellationToken ct) => attendance.SaveAsync(request, ct);

    /// <summary>Totals for the day and the classes whose register has not been taken yet.</summary>
    [HttpGet("overview")]
    [HasPermission(Permissions.Attendance.View)]
    public Task<DailyOverview> Overview([FromQuery] DateOnly? date, [FromQuery] Guid? campusId, CancellationToken ct) =>
        attendance.OverviewAsync(date ?? lookups.TodayDhaka, campusId, ct);

    [HttpGet("monthly")]
    [HasPermission(Permissions.Attendance.View)]
    public Task<MonthlyRegister> Monthly([FromQuery] Guid sectionId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken ct)
    {
        var today = lookups.TodayDhaka;
        return attendance.MonthlyAsync(sectionId, year ?? today.Year, month ?? today.Month, ct);
    }

    /// <summary>Term attendance % per student against the warning and exam-eligibility thresholds.</summary>
    [HttpGet("thresholds")]
    [HasPermission(Permissions.Attendance.View)]
    public Task<ThresholdReport> Thresholds([FromQuery] Guid? termId, [FromQuery] Guid? campusId, [FromQuery] Guid? sectionId, [FromQuery] bool belowOnly = true,
        CancellationToken ct = default) =>
        attendance.ThresholdsAsync(termId, campusId, sectionId, belowOnly, ct);

    /// <summary>Printable warning letter to the guardians.</summary>
    [HttpGet("thresholds/{studentId:guid}/letter")]
    [HasPermission(Permissions.Attendance.View)]
    public async Task<ContentResult> Letter(Guid studentId, [FromQuery] Guid? termId, CancellationToken ct) =>
        Content(await letters.BuildAsync(studentId, termId, ct), "text/html; charset=utf-8");
}

/// <summary>ATT-04: student leave applications from the school side.</summary>
[ApiController]
[Route("api/attendance/leave")]
[Tags("Attendance: Student leave")]
public sealed class StudentLeaveController(StudentLeaveService leaves) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Attendance.View)]
    public Task<List<StudentLeaveView>> List([FromQuery] LeaveStatus? status, [FromQuery] Guid? campusId, [FromQuery] Guid? sectionId, [FromQuery] Guid? studentId,
        CancellationToken ct) => leaves.ListAsync(status, campusId, sectionId, studentId, ct);

    /// <summary>Records an application received at the office (a note or call from the guardian).</summary>
    [HttpPost("{studentId:guid}")]
    [HasPermission(Permissions.Attendance.Take)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<StudentLeaveView> Apply(Guid studentId, [FromForm] StudentLeaveForm form, CancellationToken ct) => leaves.ApplyAsync(studentId, form, false, ct);

    /// <summary>Approve or refuse (class teacher of the student, or attendance.leave).</summary>
    [HttpPost("{id:guid}/decide")]
    [HasPermission(Permissions.Attendance.Take)]
    public Task<StudentLeaveView> Decide(Guid id, LeaveDecisionRequest request, CancellationToken ct) => leaves.DecideAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Attendance.Take)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await leaves.CancelAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/attachment")]
    [HasPermission(Permissions.Attendance.View)]
    public async Task<IActionResult> Attachment(Guid id, CancellationToken ct)
    {
        var (content, type, name) = await leaves.AttachmentAsync(id, false, ct);
        return File(content, type, name);
    }
}

public sealed class SaveRegisterRequestValidator : AbstractValidator<SaveRegisterRequest>
{
    public SaveRegisterRequestValidator()
    {
        RuleFor(x => x.Entries).NotEmpty();
        RuleFor(x => x.PeriodNo).GreaterThan(0).When(x => x.PeriodNo is not null);
        RuleForEach(x => x.Entries).ChildRules(e => e.RuleFor(x => x.Remarks).MaximumLength(300));
    }
}

public sealed class StudentLeaveFormValidator : AbstractValidator<StudentLeaveForm>
{
    public StudentLeaveFormValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class LeaveDecisionRequestValidator : AbstractValidator<LeaveDecisionRequest>
{
    public LeaveDecisionRequestValidator() => RuleFor(x => x.Note).MaximumLength(500);
}
