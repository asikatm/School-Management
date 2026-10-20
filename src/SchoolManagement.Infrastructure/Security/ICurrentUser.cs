using SchoolManagement.Infrastructure.Common;

namespace SchoolManagement.Infrastructure.Security;

/// <summary>
/// The caller of the current request (or the system, for background jobs).
/// Permissions are held per campus; a null campus in a grant means organisation-wide (CORE-01, CORE-03).
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    bool IsSystem { get; }
    string? IpAddress { get; }
    string Language { get; }

    /// <summary>True when the user holds at least one organisation-wide role.</summary>
    bool HasAllCampusAccess { get; }

    /// <summary>Campuses the user may see; ignored when <see cref="HasAllCampusAccess"/> is true.</summary>
    IReadOnlyCollection<Guid> CampusIds { get; }

    bool HasPermission(string permission);
    bool HasPermission(string permission, Guid campusId);
}

public static class CurrentUserExtensions
{
    public static bool CanAccessCampus(this ICurrentUser user, Guid campusId) =>
        user.IsSystem || user.HasAllCampusAccess || user.CampusIds.Contains(campusId);

    public static void EnsurePermission(this ICurrentUser user, string permission, Guid campusId)
    {
        if (!user.HasPermission(permission, campusId))
            throw new ForbiddenException($"Permission '{permission}' is required for this campus.");
    }

    public static void EnsurePermission(this ICurrentUser user, string permission)
    {
        if (!user.HasPermission(permission))
            throw new ForbiddenException($"Permission '{permission}' is required.");
    }
}

/// <summary>Identity used by background jobs, seeding and design-time tooling: full access.</summary>
public sealed class SystemUser : ICurrentUser
{
    public static readonly SystemUser Instance = new();
    public Guid? UserId => null;
    public string? UserName => "system";
    public bool IsAuthenticated => true;
    public bool IsSystem => true;
    public string? IpAddress => null;
    public string Language => "en";
    public bool HasAllCampusAccess => true;
    public IReadOnlyCollection<Guid> CampusIds => [];
    public bool HasPermission(string permission) => true;
    public bool HasPermission(string permission, Guid campusId) => true;
}
