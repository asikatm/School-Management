using System.Text.RegularExpressions;
using SchoolManagement.Api.Modules.Identity;
using SchoolManagement.Domain.Students;

namespace SchoolManagement.Api.Modules.Students.Services;

public interface IGuardianAccountService
{
    /// <summary>
    /// Ensures the guardian has a parent-portal login with the Parent role on <paramref name="campusId"/> (one family
    /// login across campuses, PRT-09). Returns the temporary password only when a new account was created. Does not save.
    /// </summary>
    Task<(string UserName, string? TemporaryPassword)> EnsureAccountAsync(Guardian guardian, Guid campusId, CancellationToken ct);
}

public sealed partial class GuardianAccountService(AppDbContext db, IPasswordHasher hasher) : IGuardianAccountService
{
    public async Task<(string UserName, string? TemporaryPassword)> EnsureAccountAsync(Guardian guardian, Guid campusId, CancellationToken ct)
    {
        var parentRoleId = await db.Roles.Where(r => r.Name == RoleNames.Parent).Select(r => r.Id).FirstAsync(ct);

        if (guardian.UserId is { } existingId)
        {
            var user = await db.Users.Include(u => u.RoleAssignments).FirstAsync(u => u.Id == existingId, ct);
            if (!user.RoleAssignments.Any(a => a.RoleId == parentRoleId && a.CampusId == campusId))
                db.UserRoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = parentRoleId, CampusId = campusId });
            return (user.UserName, null);
        }

        var baseName = NonDigits().Replace(guardian.Phone, "");
        if (baseName.StartsWith("88")) baseName = baseName[2..];
        var userName = baseName;
        for (var i = 2; await db.Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == userName, ct) || db.Users.Local.Any(u => u.UserName == userName); i++)
            userName = $"{baseName}-{i}";

        var password = UsersController.GenerateTemporaryPassword();
        var newUser = new User
        {
            UserName = userName,
            FullName = guardian.Name,
            Phone = guardian.Phone,
            Email = guardian.Email,
            UserType = UserType.Guardian,
            PasswordHash = hasher.Hash(password),
            MustChangePassword = true,
            PreferredLanguage = "bn"
        };
        newUser.RoleAssignments.Add(new UserRoleAssignment { UserId = newUser.Id, RoleId = parentRoleId, CampusId = campusId });
        db.Users.Add(newUser);
        guardian.UserId = newUser.Id;
        return (userName, password);
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();
}
