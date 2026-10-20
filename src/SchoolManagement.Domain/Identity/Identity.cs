using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Identity;

public enum UserType
{
    Staff = 0,
    Guardian = 1,
    Student = 2
}

[Audited]
public class User : SoftDeleteEntity
{
    public required string UserName { get; set; }
    public required string FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public required string PasswordHash { get; set; }
    public UserType UserType { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public string PreferredLanguage { get; set; } = "en";

    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }

    /// <summary>Changes on password reset or role change so outstanding tokens can be rejected.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    public ICollection<UserRoleAssignment> RoleAssignments { get; set; } = [];
}

[Audited]
public class Role : SoftDeleteEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>System roles ship with the product and cannot be deleted (spec 5.6).</summary>
    public bool IsSystem { get; set; }
    public ICollection<RolePermission> Permissions { get; set; } = [];
}

public class RolePermission : Entity
{
    public Guid RoleId { get; set; }
    public required string Permission { get; set; }
}

/// <summary>
/// CORE-03: a role held by a user, scoped to one campus or (when <see cref="CampusId"/> is null) the whole organisation.
/// </summary>
[Audited]
public class UserRoleAssignment : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public Guid? CampusId { get; set; }
}

public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedById { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}
