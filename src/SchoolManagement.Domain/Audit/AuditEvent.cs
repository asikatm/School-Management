namespace SchoolManagement.Domain.Audit;

public enum AuditAction
{
    Create = 0,
    Update = 1,
    Delete = 2,
    Login = 3,
    LoginFailed = 4,
    Export = 5,
    PermissionChange = 6,
    Custom = 7
}

/// <summary>
/// CORE-05: append-only audit trail. <see cref="Id"/> is a database identity giving a total order;
/// a background sealer then chains each row's hash to the previous one so later tampering is detectable.
/// </summary>
public class AuditEvent
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? IpAddress { get; set; }
    public Guid? CampusId { get; set; }
    public AuditAction Action { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? Description { get; set; }

    /// <summary>Null until sealed.</summary>
    public string? PreviousHash { get; set; }
    public string? Hash { get; set; }
}
