using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Domain.Approvals;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Common;
using SchoolManagement.Infrastructure.Jobs;
using SchoolManagement.Infrastructure.Notifications;
using SchoolManagement.Infrastructure.Persistence;
using SchoolManagement.Infrastructure.Security;

namespace SchoolManagement.Infrastructure.Approvals;

/// <summary>Module hook invoked when a request of its type reaches a final decision.</summary>
public interface IApprovalHandler
{
    string RequestType { get; }
    Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct);
    Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct);
}

public interface IApprovalService
{
    /// <summary>
    /// Starts the configured workflow for <paramref name="requestType"/>. Returns null when no workflow is
    /// configured, meaning the action needs no approval. The caller saves the context.
    /// </summary>
    Task<ApprovalRequest?> SubmitAsync(string requestType, string entityType, string entityId, Guid campusId, string? summary, CancellationToken ct);

    /// <summary>Records the current user's decision on the current step and saves.</summary>
    Task<ApprovalRequest> DecideAsync(Guid requestId, bool approve, string? comment, CancellationToken ct);

    /// <summary>Pending requests whose current step the current user (or someone they deputise for) can decide.</summary>
    Task<IReadOnlyList<ApprovalRequest>> GetActionableAsync(CancellationToken ct);
}

/// <summary>CORE-04: configurable multi-step approvals with delegation and escalation.</summary>
public sealed class ApprovalService(
    AppDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    INotificationService notifications,
    IEnumerable<IApprovalHandler> handlers) : IApprovalService
{
    public async Task<ApprovalRequest?> SubmitAsync(string requestType, string entityType, string entityId, Guid campusId, string? summary, CancellationToken ct)
    {
        var workflow = await db.ApprovalWorkflows.Include(w => w.Steps)
            .Where(w => w.RequestType == requestType && w.IsActive && (w.CampusId == campusId || w.CampusId == null))
            .OrderByDescending(w => w.CampusId != null)
            .FirstOrDefaultAsync(ct);
        if (workflow is null || workflow.Steps.Count == 0) return null;

        var hasOpen = await db.ApprovalRequests.AnyAsync(r =>
            r.EntityType == entityType && r.EntityId == entityId && r.RequestType == requestType && r.Status == ApprovalStatus.Pending, ct);
        if (hasOpen)
            throw new DomainException("An approval request for this item is already pending.", "approval_pending", isConflict: true);

        var now = clock.GetUtcNow().UtcDateTime;
        var request = new ApprovalRequest
        {
            WorkflowId = workflow.Id,
            CampusId = campusId,
            RequestType = requestType,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            RequestedBy = currentUser.UserId ?? Guid.Empty,
            CurrentStepOrder = workflow.Steps.Min(s => s.Order),
            CurrentStepStartedAtUtc = now
        };
        db.ApprovalRequests.Add(request);

        var step = workflow.Steps.First(s => s.Order == request.CurrentStepOrder);
        await NotifyApproversAsync(request, step.ApproverRoleId, ct);
        return request;
    }

    public async Task<ApprovalRequest> DecideAsync(Guid requestId, bool approve, string? comment, CancellationToken ct)
    {
        var request = await db.ApprovalRequests
            .Include(r => r.Workflow).ThenInclude(w => w!.Steps)
            .Include(r => r.Decisions)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new NotFoundException("Approval request", requestId);

        if (request.Status != ApprovalStatus.Pending)
            throw new DomainException("This request has already been decided.", "approval_closed", isConflict: true);

        var step = request.Workflow!.Steps.First(s => s.Order == request.CurrentStepOrder);
        var onBehalfOf = await ResolveDeciderAsync(request, step, ct)
            ?? throw new ForbiddenException("You are not an approver for the current step of this request.");

        var now = clock.GetUtcNow().UtcDateTime;
        request.Decisions.Add(new ApprovalDecision
        {
            RequestId = request.Id,
            StepOrder = step.Order,
            DecidedBy = currentUser.UserId,
            OnBehalfOf = onBehalfOf == currentUser.UserId ? null : onBehalfOf,
            Decision = approve ? ApprovalDecisionType.Approve : ApprovalDecisionType.Reject,
            Comment = comment,
            DecidedAtUtc = now
        });

        var handler = handlers.FirstOrDefault(h => h.RequestType == request.RequestType);
        if (!approve)
        {
            request.Status = ApprovalStatus.Rejected;
            request.CompletedAtUtc = now;
            if (handler is not null) await handler.OnRejectedAsync(request, ct);
            NotifyRequester(request, "rejected");
        }
        else
        {
            var next = request.Workflow.Steps.Where(s => s.Order > step.Order).OrderBy(s => s.Order).FirstOrDefault();
            if (next is null)
            {
                request.Status = ApprovalStatus.Approved;
                request.CompletedAtUtc = now;
                if (handler is not null) await handler.OnApprovedAsync(request, ct);
                NotifyRequester(request, "approved");
            }
            else
            {
                request.CurrentStepOrder = next.Order;
                request.CurrentStepStartedAtUtc = now;
                request.IsEscalated = false;
                await NotifyApproversAsync(request, next.ApproverRoleId, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<IReadOnlyList<ApprovalRequest>> GetActionableAsync(CancellationToken ct)
    {
        var deciders = await DeciderUserIdsAsync(null, ct);
        var assignments = await db.UserRoleAssignments.AsNoTracking()
            .Where(a => deciders.Contains(a.UserId))
            .Select(a => new { a.RoleId, a.CampusId })
            .ToListAsync(ct);
        if (assignments.Count == 0) return [];

        var pending = await db.ApprovalRequests.AsNoTracking()
            .Include(r => r.Workflow).ThenInclude(w => w!.Steps)
            .Where(r => r.Status == ApprovalStatus.Pending)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        return pending.Where(r =>
        {
            var step = r.Workflow!.Steps.First(s => s.Order == r.CurrentStepOrder);
            var roleId = r.IsEscalated && step.EscalateToRoleId is { } esc ? esc : step.ApproverRoleId;
            return assignments.Any(a => a.RoleId == roleId && (a.CampusId == null || a.CampusId == r.CampusId));
        }).ToList();
    }

    /// <summary>Returns the user the decision is made as (the caller or a delegator), or null if not allowed.</summary>
    private async Task<Guid?> ResolveDeciderAsync(ApprovalRequest request, ApprovalStep step, CancellationToken ct)
    {
        var roleIds = new List<Guid> { step.ApproverRoleId };
        if (request.IsEscalated && step.EscalateToRoleId is { } esc) roleIds.Add(esc);

        // The caller is checked first, so acting in one's own right wins over acting as a delegate.
        foreach (var candidate in await DeciderUserIdsAsync(request.RequestType, ct))
        {
            var holdsRole = await db.UserRoleAssignments.AnyAsync(a =>
                a.UserId == candidate && roleIds.Contains(a.RoleId) && (a.CampusId == null || a.CampusId == request.CampusId), ct);
            if (holdsRole) return candidate;
        }
        return null;
    }

    /// <summary>The current user plus everyone who has delegated approvals to them right now.</summary>
    private async Task<List<Guid>> DeciderUserIdsAsync(string? requestType, CancellationToken ct)
    {
        if (currentUser.UserId is not { } me) return [];
        var now = clock.GetUtcNow().UtcDateTime;
        var delegators = await db.ApprovalDelegations.AsNoTracking()
            .Where(d => d.ToUserId == me && d.StartsAtUtc <= now && d.EndsAtUtc >= now &&
                        (d.RequestType == null || requestType == null || d.RequestType == requestType))
            .Select(d => d.FromUserId)
            .ToListAsync(ct);
        return [me, .. delegators];
    }

    private async Task NotifyApproversAsync(ApprovalRequest request, Guid roleId, CancellationToken ct)
    {
        var approverIds = await db.UserRoleAssignments.AsNoTracking()
            .Where(a => a.RoleId == roleId && (a.CampusId == null || a.CampusId == request.CampusId))
            .Select(a => a.UserId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var userId in approverIds)
        {
            notifications.QueueRaw(NotificationChannel.InApp, userId.ToString(),
                $"Approval needed: {request.RequestType} — {request.Summary}", "Approval needed",
                campusId: request.CampusId, recipientUserId: userId);
        }
    }

    private void NotifyRequester(ApprovalRequest request, string outcome)
    {
        if (request.RequestedBy == Guid.Empty) return;
        notifications.QueueRaw(NotificationChannel.InApp, request.RequestedBy.ToString(),
            $"Your {request.RequestType} request was {outcome}. {request.Summary}", $"Request {outcome}",
            campusId: request.CampusId, recipientUserId: request.RequestedBy);
    }
}

/// <summary>Escalates pending requests whose current step has waited longer than its escalation timer.</summary>
public sealed class ApprovalEscalationJob : IScheduledJob
{
    public string Name => "approval-escalation";
    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var notifications = services.GetRequiredService<INotificationService>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var pending = await db.ApprovalRequests
            .Include(r => r.Workflow).ThenInclude(w => w!.Steps)
            .Include(r => r.Decisions)
            .Where(r => r.Status == ApprovalStatus.Pending && !r.IsEscalated)
            .ToListAsync(ct);

        foreach (var request in pending)
        {
            var step = request.Workflow!.Steps.First(s => s.Order == request.CurrentStepOrder);
            if (step.EscalateAfterHours is not { } hours || step.EscalateToRoleId is not { } escalateTo) continue;
            if (request.CurrentStepStartedAtUtc.AddHours(hours) > now) continue;

            request.IsEscalated = true;
            request.Decisions.Add(new ApprovalDecision
            {
                RequestId = request.Id,
                StepOrder = step.Order,
                Decision = ApprovalDecisionType.Escalated,
                Comment = $"Auto-escalated after {hours} h without a decision.",
                DecidedAtUtc = now
            });

            var users = await db.UserRoleAssignments.AsNoTracking()
                .Where(a => a.RoleId == escalateTo && (a.CampusId == null || a.CampusId == request.CampusId))
                .Select(a => a.UserId).Distinct().ToListAsync(ct);
            foreach (var userId in users)
                notifications.QueueRaw(NotificationChannel.InApp, userId.ToString(),
                    $"Escalated approval: {request.RequestType} — {request.Summary}", "Escalated approval",
                    MessagePriority.High, request.CampusId, userId);
        }

        await db.SaveChangesAsync(ct);
    }
}
