using SchoolManagement.Domain.Approvals;
using SchoolManagement.Infrastructure.Approvals;

namespace SchoolManagement.Api.Modules.Core;

/// <summary>CORE-04: approval workflow configuration, approver inbox, decisions and delegation.</summary>
[ApiController]
[Route("api/approvals")]
[Tags("Core: Approvals")]
public sealed class ApprovalsController(AppDbContext db, IApprovalService approvals, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("workflows")]
    [HasPermission(Permissions.Approvals.View)]
    public async Task<List<WorkflowView>> Workflows(CancellationToken ct) =>
        (await db.ApprovalWorkflows.AsNoTracking().Include(w => w.Steps).OrderBy(w => w.RequestType).ToListAsync(ct)).Select(ToView).ToList();

    [HttpPost("workflows")]
    [HasPermission(Permissions.Approvals.Configure)]
    public async Task<WorkflowView> CreateWorkflow(WorkflowRequest request, CancellationToken ct)
    {
        var wf = new ApprovalWorkflow { RequestType = request.RequestType, CampusId = request.CampusId, Name = request.Name, IsActive = request.IsActive };
        await SetStepsAsync(wf, request.Steps, ct);
        db.ApprovalWorkflows.Add(wf);
        await db.SaveChangesAsync(ct);
        return ToView(wf);
    }

    /// <summary>Replaces the workflow's steps. Requests already in flight keep following the step orders they started with.</summary>
    [HttpPut("workflows/{id:guid}")]
    [HasPermission(Permissions.Approvals.Configure)]
    public async Task<WorkflowView> UpdateWorkflow(Guid id, WorkflowRequest request, CancellationToken ct)
    {
        var wf = await db.ApprovalWorkflows.Include(w => w.Steps).FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Workflow", id);
        if (await db.ApprovalRequests.AnyAsync(r => r.WorkflowId == id && r.Status == ApprovalStatus.Pending, ct))
            throw new DomainException("Requests are pending on this workflow; create a new workflow version instead.", "workflow_in_use", isConflict: true);
        wf.Name = request.Name;
        wf.RequestType = request.RequestType;
        wf.CampusId = request.CampusId;
        wf.IsActive = request.IsActive;
        db.ApprovalSteps.RemoveRange(wf.Steps);
        wf.Steps.Clear();
        await SetStepsAsync(wf, request.Steps, ct);
        await db.SaveChangesAsync(ct);
        return ToView(wf);
    }

    /// <summary>Requests waiting for the current user (directly or as a delegate).</summary>
    [HttpGet("inbox")]
    [Authorize]
    public async Task<List<ApprovalRequestView>> Inbox(CancellationToken ct) =>
        (await approvals.GetActionableAsync(ct)).Select(ToView).ToList();

    [HttpGet("requests")]
    [HasPermission(Permissions.Approvals.View)]
    public async Task<PagedResult<ApprovalRequestView>> Requests([FromQuery] ApprovalStatus? status, [FromQuery] string? requestType, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var q = db.ApprovalRequests.AsNoTracking().Include(r => r.Decisions).AsQueryable();
        if (status is not null) q = q.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(requestType)) q = q.Where(r => r.RequestType == requestType);
        var result = await q.OrderByDescending(r => r.CreatedAtUtc).ToPagedAsync(page, ct);
        return new PagedResult<ApprovalRequestView>(result.Items.Select(ToView).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    [HttpPost("requests/{id:guid}/decide")]
    [Authorize]
    public async Task<ApprovalRequestView> Decide(Guid id, DecisionRequest request, CancellationToken ct) =>
        ToView(await approvals.DecideAsync(id, request.Approve, request.Comment, ct));

    [HttpGet("delegations")]
    [Authorize]
    public async Task<List<DelegationView>> MyDelegations(CancellationToken ct) =>
        await db.ApprovalDelegations.AsNoTracking().Where(d => d.FromUserId == currentUser.UserId || d.ToUserId == currentUser.UserId)
            .OrderByDescending(d => d.StartsAtUtc)
            .Select(d => new DelegationView(d.Id, d.FromUserId, d.ToUserId, d.StartsAtUtc, d.EndsAtUtc, d.RequestType)).ToListAsync(ct);

    /// <summary>Hands the caller's approvals to a colleague for a period (e.g. while on leave).</summary>
    [HttpPost("delegations")]
    [Authorize]
    public async Task<DelegationView> Delegate(DelegationRequest request, CancellationToken ct)
    {
        if (request.ToUserId == currentUser.UserId) throw new DomainException("You cannot delegate to yourself.", "invalid_delegate");
        if (!await db.Users.AnyAsync(u => u.Id == request.ToUserId && u.IsActive, ct)) throw new NotFoundException("User", request.ToUserId);
        var d = new ApprovalDelegation
        {
            FromUserId = currentUser.UserId!.Value, ToUserId = request.ToUserId,
            StartsAtUtc = request.StartsAtUtc, EndsAtUtc = request.EndsAtUtc, RequestType = request.RequestType
        };
        db.ApprovalDelegations.Add(d);
        await db.SaveChangesAsync(ct);
        return new DelegationView(d.Id, d.FromUserId, d.ToUserId, d.StartsAtUtc, d.EndsAtUtc, d.RequestType);
    }

    [HttpDelete("delegations/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> RemoveDelegation(Guid id, CancellationToken ct)
    {
        var d = await db.ApprovalDelegations.FirstOrDefaultAsync(x => x.Id == id && x.FromUserId == currentUser.UserId, ct) ?? throw new NotFoundException("Delegation", id);
        db.ApprovalDelegations.Remove(d);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task SetStepsAsync(ApprovalWorkflow wf, List<WorkflowStepRequest> steps, CancellationToken ct)
    {
        var roleIds = steps.SelectMany(s => new[] { s.ApproverRoleId, s.EscalateToRoleId }).OfType<Guid>().Distinct().ToList();
        if (await db.Roles.CountAsync(r => roleIds.Contains(r.Id), ct) != roleIds.Count)
            throw new DomainException("One or more roles do not exist.", "invalid_role");
        var order = 1;
        foreach (var s in steps)
            wf.Steps.Add(new ApprovalStep
            {
                WorkflowId = wf.Id, Order = order++, Name = s.Name, ApproverRoleId = s.ApproverRoleId,
                EscalateAfterHours = s.EscalateAfterHours, EscalateToRoleId = s.EscalateToRoleId
            });
    }

    private static WorkflowView ToView(ApprovalWorkflow w) => new(w.Id, w.RequestType, w.CampusId, w.Name, w.IsActive,
        w.Steps.OrderBy(s => s.Order).Select(s => new WorkflowStepView(s.Order, s.Name, s.ApproverRoleId, s.EscalateAfterHours, s.EscalateToRoleId)).ToList());

    private static ApprovalRequestView ToView(ApprovalRequest r) => new(r.Id, r.RequestType, r.EntityType, r.EntityId, r.CampusId, r.Summary,
        r.RequestedBy, r.CurrentStepOrder, r.IsEscalated, r.Status, r.CreatedAtUtc, r.CompletedAtUtc,
        r.Decisions.OrderBy(d => d.DecidedAtUtc).Select(d => new DecisionView(d.StepOrder, d.DecidedBy, d.OnBehalfOf, d.Decision, d.Comment, d.DecidedAtUtc)).ToList());
}

public record WorkflowStepView(int Order, string Name, Guid ApproverRoleId, int? EscalateAfterHours, Guid? EscalateToRoleId);
public record WorkflowView(Guid Id, string RequestType, Guid? CampusId, string Name, bool IsActive, IReadOnlyList<WorkflowStepView> Steps);
public record WorkflowStepRequest(string Name, Guid ApproverRoleId, int? EscalateAfterHours, Guid? EscalateToRoleId);
public record WorkflowRequest(string RequestType, Guid? CampusId, string Name, bool IsActive, List<WorkflowStepRequest> Steps);
public record DecisionView(int StepOrder, Guid? DecidedBy, Guid? OnBehalfOf, ApprovalDecisionType Decision, string? Comment, DateTime DecidedAtUtc);
public record ApprovalRequestView(Guid Id, string RequestType, string EntityType, string EntityId, Guid CampusId, string? Summary, Guid RequestedBy,
    int CurrentStep, bool IsEscalated, ApprovalStatus Status, DateTime CreatedAtUtc, DateTime? CompletedAtUtc, IReadOnlyList<DecisionView> Decisions);
public record DecisionRequest(bool Approve, string? Comment);
public record DelegationRequest(Guid ToUserId, DateTime StartsAtUtc, DateTime EndsAtUtc, string? RequestType);
public record DelegationView(Guid Id, Guid FromUserId, Guid ToUserId, DateTime StartsAtUtc, DateTime EndsAtUtc, string? RequestType);

public sealed class WorkflowRequestValidator : AbstractValidator<WorkflowRequest>
{
    public WorkflowRequestValidator()
    {
        RuleFor(x => x.RequestType).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Steps).NotEmpty();
        RuleForEach(x => x.Steps).ChildRules(s =>
        {
            s.RuleFor(x => x.Name).NotEmpty();
            s.RuleFor(x => x.EscalateAfterHours).GreaterThan(0).When(x => x.EscalateAfterHours is not null);
            s.RuleFor(x => x.EscalateToRoleId).NotNull().When(x => x.EscalateAfterHours is not null)
                .WithMessage("An escalation role is required when an escalation timer is set.");
        });
    }
}

public sealed class DecisionRequestValidator : AbstractValidator<DecisionRequest>
{
    public DecisionRequestValidator() =>
        RuleFor(x => x.Comment).NotEmpty().When(x => !x.Approve).WithMessage("A comment is required when rejecting.");
}

public sealed class DelegationRequestValidator : AbstractValidator<DelegationRequest>
{
    public DelegationRequestValidator() => RuleFor(x => x.EndsAtUtc).GreaterThan(x => x.StartsAtUtc);
}
