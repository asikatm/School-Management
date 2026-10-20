using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Approvals;

public static class ApprovalTypes
{
    public const string StudentLeave = "StudentLeave";
    public const string StaffLeave = "StaffLeave";
    public const string FeeWaiver = "FeeWaiver";
    public const string Discount = "Discount";
    public const string Purchase = "Purchase";
    public const string CertificateIssue = "CertificateIssue";
    public const string Withdrawal = "Withdrawal";
    public const string ProfileUpdate = "ProfileUpdate";
}

/// <summary>CORE-04: a configurable multi-step approval chain for one request type, optionally per campus.</summary>
[Audited]
public class ApprovalWorkflow : SoftDeleteEntity
{
    public required string RequestType { get; set; }
    public Guid? CampusId { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<ApprovalStep> Steps { get; set; } = [];
}

public class ApprovalStep : Entity
{
    public Guid WorkflowId { get; set; }
    public int Order { get; set; }
    public required string Name { get; set; }
    public Guid ApproverRoleId { get; set; }

    /// <summary>When set, a pending request escalates to <see cref="EscalateToRoleId"/> after this many hours.</summary>
    public int? EscalateAfterHours { get; set; }
    public Guid? EscalateToRoleId { get; set; }
}

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}

public class ApprovalRequest : CampusEntity
{
    public Guid WorkflowId { get; set; }
    public ApprovalWorkflow? Workflow { get; set; }
    public required string RequestType { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public string? Summary { get; set; }
    public Guid RequestedBy { get; set; }
    public int CurrentStepOrder { get; set; } = 1;
    public DateTime CurrentStepStartedAtUtc { get; set; }
    public bool IsEscalated { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public DateTime? CompletedAtUtc { get; set; }
    public ICollection<ApprovalDecision> Decisions { get; set; } = [];
}

public enum ApprovalDecisionType
{
    Approve = 0,
    Reject = 1,
    Escalated = 2
}

public class ApprovalDecision : Entity
{
    public Guid RequestId { get; set; }
    public int StepOrder { get; set; }
    public Guid? DecidedBy { get; set; }
    public Guid? OnBehalfOf { get; set; }
    public ApprovalDecisionType Decision { get; set; }
    public string? Comment { get; set; }
    public DateTime DecidedAtUtc { get; set; }
}

/// <summary>Lets an approver hand their approvals to a colleague for a date range (CORE-04 delegation).</summary>
public class ApprovalDelegation : AuditableEntity
{
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public string? RequestType { get; set; }
}
