namespace SchoolManagement.Api.Modules.Academics;

/// <summary>ACA-05: cover for absent teachers.</summary>
[ApiController]
[Route("api/academics/substitutions")]
[Tags("Academics: Substitution")]
public sealed class SubstitutionsController(SubstitutionService substitutions, AcademicLookups lookups) : ControllerBase
{
    /// <summary>The absent teacher's periods on a date, each with any cover already booked and ranked free teachers.</summary>
    [HttpGet("needs")]
    [HasPermission(Permissions.Academic.Substitutions)]
    public Task<CoverNeeds> Needs([FromQuery] Guid teacherUserId, [FromQuery] DateOnly? date, CancellationToken ct) =>
        substitutions.NeedsAsync(teacherUserId, date ?? lookups.TodayDhaka, ct);

    [HttpPost]
    [HasPermission(Permissions.Academic.Substitutions)]
    public Task<SubstitutionView> Assign(AssignCoverRequest request, CancellationToken ct) => substitutions.AssignAsync(request, ct);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Academic.Substitutions)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await substitutions.CancelAsync(id, ct);
        return NoContent();
    }

    [HttpGet]
    [HasPermission(Permissions.Academic.View)]
    public Task<List<SubstitutionView>> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? campusId, CancellationToken ct)
    {
        var today = lookups.TodayDhaka;
        return substitutions.ListAsync(from ?? today, to ?? from ?? today, campusId, ct);
    }

    /// <summary>Cover periods given and received per teacher (for cover-period pay, HRM-04).</summary>
    [HttpGet("report")]
    [HasPermission(Permissions.Academic.View)]
    public Task<List<CoverReportRow>> Report([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? campusId, CancellationToken ct) =>
        substitutions.ReportAsync(from, to, campusId, ct);
}

public sealed class AssignCoverRequestValidator : AbstractValidator<AssignCoverRequest>
{
    public AssignCoverRequestValidator() => RuleFor(x => x.Reason).MaximumLength(300);
}
