using SchoolManagement.Api.Modules.Identity;

namespace SchoolManagement.Api.Modules.Students;

public record StudentPortalAccount(string UserName, string? TemporaryPassword, bool Created);

/// <summary>PRT: a senior student's own portal login (homework, resources, timetable, published results).</summary>
[ApiController]
[Route("api/students/{studentId:guid}/portal-account")]
[Tags("Students")]
public sealed class StudentPortalAccountController(AppDbContext db, IPasswordHasher hasher, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Students.View)]
    public async Task<ActionResult<StudentPortalAccount>> Get(Guid studentId, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct) ?? throw new NotFoundException("Student", studentId);
        if (student.UserId is not { } userId) return NoContent();
        var userName = await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.UserName).FirstAsync(ct);
        return new StudentPortalAccount(userName, null, false);
    }

    /// <summary>Creates the login (user name = admission number) with a temporary password, or returns the existing one.</summary>
    [HttpPost]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<StudentPortalAccount> Create(Guid studentId, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct) ?? throw new NotFoundException("Student", studentId);
        currentUser.EnsurePermission(Permissions.Students.Edit, student.CampusId);
        var roleId = await db.Roles.Where(r => r.Name == RoleNames.Student).Select(r => r.Id).FirstAsync(ct);
        if (student.UserId is { } existing)
        {
            var user = await db.Users.Include(u => u.RoleAssignments).FirstAsync(u => u.Id == existing, ct);
            if (!user.RoleAssignments.Any(a => a.RoleId == roleId && a.CampusId == student.CampusId))
            {
                db.UserRoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = roleId, CampusId = student.CampusId });
                await db.SaveChangesAsync(ct);
            }
            return new StudentPortalAccount(user.UserName, null, false);
        }

        var baseName = student.AdmissionNo.ToLowerInvariant();
        var userName = baseName;
        for (var i = 2; await db.Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == userName, ct); i++) userName = $"{baseName}-{i}";
        var password = UsersController.GenerateTemporaryPassword();
        var account = new User
        {
            UserName = userName, FullName = student.FullName, Email = student.Email, Phone = student.Phone, UserType = UserType.Student,
            PasswordHash = hasher.Hash(password), MustChangePassword = true
        };
        account.RoleAssignments.Add(new UserRoleAssignment { UserId = account.Id, RoleId = roleId, CampusId = student.CampusId });
        db.Users.Add(account);
        student.UserId = account.Id;
        await db.SaveChangesAsync(ct);
        return new StudentPortalAccount(userName, password, true);
    }
}
