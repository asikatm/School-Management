using System.Security.Cryptography;
using SchoolManagement.Domain.Audit;
using SchoolManagement.Infrastructure.Auditing;

namespace SchoolManagement.Api.Modules.Identity;

/// <summary>CORE-03: staff accounts and their campus-scoped role assignments.</summary>
[ApiController]
[Route("api/users")]
[Tags("Core: Users & Roles")]
public sealed class UsersController(AppDbContext db, IPasswordHasher hasher, IAuditLog audit, TokenService tokens) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    public async Task<PagedResult<UserListItem>> List([FromQuery] string? search, [FromQuery] UserType? type, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var q = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(u => u.UserName.Contains(search) || u.FullName.Contains(search) || (u.Email != null && u.Email.Contains(search)) || (u.Phone != null && u.Phone.Contains(search)));
        if (type is not null) q = q.Where(u => u.UserType == type);
        return await q.OrderBy(u => u.FullName)
            .Select(u => new UserListItem(u.Id, u.UserName, u.FullName, u.Email, u.Phone, u.UserType, u.IsActive, u.LockoutEndUtc, u.LastLoginUtc))
            .ToPagedAsync(page, ct);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Users.View)]
    public async Task<UserDetail> Get(Guid id, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().Include(x => x.RoleAssignments).ThenInclude(a => a.Role)
                    .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User", id);
        return ToDetail(u);
    }

    [HttpPost]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<ActionResult<CreateUserResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == request.UserName, ct))
            throw new DomainException($"User name '{request.UserName}' is already taken.", "duplicate", isConflict: true);

        var password = request.Password ?? GenerateTemporaryPassword();
        var problems = PasswordPolicy.Validate(password);
        if (problems.Count > 0) throw new DomainException(string.Join(" ", problems), "weak_password");

        var user = new User
        {
            UserName = request.UserName.Trim(),
            FullName = request.FullName.Trim(),
            Email = request.Email,
            Phone = request.Phone,
            UserType = request.UserType,
            PreferredLanguage = request.Language ?? "en",
            PasswordHash = hasher.Hash(password),
            MustChangePassword = true
        };
        db.Users.Add(user);
        await ApplyRolesAsync(user, request.Roles ?? [], ct);
        await db.SaveChangesAsync(ct);

        var created = await Get(user.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, new CreateUserResponse(created, request.Password is null ? password : null));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<UserDetail> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        user.FullName = request.FullName.Trim();
        user.Email = request.Email;
        user.Phone = request.Phone;
        user.PreferredLanguage = request.Language ?? user.PreferredLanguage;
        if (user.IsActive != request.IsActive)
        {
            user.IsActive = request.IsActive;
            user.SecurityStamp = Guid.NewGuid();
            if (!request.IsActive) await tokens.RevokeAllAsync(user.Id, ct);
        }
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    /// <summary>Replaces the user's role assignments. A null campus grants the role across all campuses.</summary>
    [HttpPut("{id:guid}/roles")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<UserDetail> SetRoles(Guid id, List<RoleAssignmentRequest> roles, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.RoleAssignments).FirstOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw new NotFoundException("User", id);
        db.UserRoleAssignments.RemoveRange(user.RoleAssignments);
        user.RoleAssignments.Clear();
        await ApplyRolesAsync(user, roles, ct);
        user.SecurityStamp = Guid.NewGuid();
        audit.Add(AuditAction.PermissionChange, "User", user.Id.ToString(),
            "Roles set to: " + string.Join(", ", roles.Select(r => $"{r.RoleId}@{r.CampusId?.ToString() ?? "*"}")));
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    [HttpPost("{id:guid}/reset-password")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<ResetPasswordResponse> ResetPassword(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        var password = GenerateTemporaryPassword();
        user.PasswordHash = hasher.Hash(password);
        user.MustChangePassword = true;
        user.SecurityStamp = Guid.NewGuid();
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync(ct);
        await tokens.RevokeAllAsync(user.Id, ct);
        return new ResetPasswordResponse(password);
    }

    [HttpPost("{id:guid}/unlock")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        user.LockoutEndUtc = null;
        user.AccessFailedCount = 0;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task ApplyRolesAsync(User user, IEnumerable<RoleAssignmentRequest> roles, CancellationToken ct)
    {
        var list = roles.DistinctBy(r => (r.RoleId, r.CampusId)).ToList();
        var roleIds = list.Select(r => r.RoleId).Distinct().ToList();
        var known = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        if (known.Count != roleIds.Count) throw new DomainException("One or more roles do not exist.", "invalid_role");

        var campusIds = list.Where(r => r.CampusId is not null).Select(r => r.CampusId!.Value).Distinct().ToList();
        var knownCampuses = await db.Campuses.CountAsync(c => campusIds.Contains(c.Id), ct);
        if (knownCampuses != campusIds.Count) throw new DomainException("One or more campuses do not exist.", "invalid_campus");

        foreach (var r in list)
            user.RoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = r.RoleId, CampusId = r.CampusId });
    }

    internal static string GenerateTemporaryPassword()
    {
        const string letters = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = i % 3 == 2 ? digits[RandomNumberGenerator.GetInt32(digits.Length)] : letters[RandomNumberGenerator.GetInt32(letters.Length)];
        return new string(chars);
    }

    private static UserDetail ToDetail(User u) => new(u.Id, u.UserName, u.FullName, u.Email, u.Phone, u.UserType, u.IsActive,
        u.MustChangePassword, u.PreferredLanguage, u.LockoutEndUtc, u.LastLoginUtc,
        u.RoleAssignments.Select(a => new RoleAssignmentView(a.RoleId, a.Role?.Name ?? "", a.CampusId)).ToList());
}

public record UserListItem(Guid Id, string UserName, string FullName, string? Email, string? Phone, UserType UserType, bool IsActive, DateTime? LockoutEndUtc, DateTime? LastLoginUtc);
public record RoleAssignmentView(Guid RoleId, string RoleName, Guid? CampusId);
public record UserDetail(Guid Id, string UserName, string FullName, string? Email, string? Phone, UserType UserType, bool IsActive,
    bool MustChangePassword, string Language, DateTime? LockoutEndUtc, DateTime? LastLoginUtc, IReadOnlyList<RoleAssignmentView> Roles);
public record RoleAssignmentRequest(Guid RoleId, Guid? CampusId);
public record CreateUserRequest(string UserName, string FullName, string? Email, string? Phone, UserType UserType, string? Password, string? Language, List<RoleAssignmentRequest>? Roles);
public record CreateUserResponse(UserDetail User, string? TemporaryPassword);
public record UpdateUserRequest(string FullName, string? Email, string? Phone, string? Language, bool IsActive);
public record ResetPasswordResponse(string TemporaryPassword);

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(128).Matches("^[A-Za-z0-9._@-]+$");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).Matches(@"^\+?[0-9]{8,15}$").When(x => !string.IsNullOrEmpty(x.Phone));
        RuleFor(x => x.Language).Must(l => l is null or "en" or "bn").WithMessage("Language must be 'en' or 'bn'.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Phone).Matches(@"^\+?[0-9]{8,15}$").When(x => !string.IsNullOrEmpty(x.Phone));
    }
}
