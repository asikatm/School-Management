using SchoolManagement.Domain.Audit;
using SchoolManagement.Infrastructure.Auditing;

namespace SchoolManagement.Api.Modules.Identity;

/// <summary>CORE-03: roles and their permissions (per module, per action). Campus scope is set on the user's assignment.</summary>
[ApiController]
[Route("api/roles")]
[Tags("Core: Users & Roles")]
public sealed class RolesController(AppDbContext db, IAuditLog audit) : ControllerBase
{
    /// <summary>The catalogue of permission codes ("module.action").</summary>
    [HttpGet("permissions")]
    [HasPermission(Permissions.Users.View)]
    public IReadOnlyList<string> PermissionCatalogue() => Permissions.All;

    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    public async Task<List<RoleView>> List(CancellationToken ct) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new RoleView(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions.Select(p => p.Permission).OrderBy(p => p).ToList()))
            .ToListAsync(ct);

    [HttpPost]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<RoleView> Create(RoleRequest request, CancellationToken ct)
    {
        if (await db.Roles.IgnoreQueryFilters().AnyAsync(r => r.Name == request.Name, ct))
            throw new DomainException($"Role '{request.Name}' already exists.", "duplicate", isConflict: true);
        var role = new Role { Name = request.Name.Trim(), Description = request.Description };
        foreach (var p in Validate(request.Permissions))
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
        db.Roles.Add(role);
        audit.Add(AuditAction.PermissionChange, "Role", role.Id.ToString(), $"Created with: {string.Join(", ", request.Permissions)}");
        await db.SaveChangesAsync(ct);
        return new RoleView(role.Id, role.Name, role.Description, role.IsSystem, role.Permissions.Select(p => p.Permission).Order().ToList());
    }

    /// <summary>Replaces the role's permissions. System roles keep their name but their permissions can be tuned (spec 5.6).</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<RoleView> Update(Guid id, RoleRequest request, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role", id);
        if (!role.IsSystem) role.Name = request.Name.Trim();
        role.Description = request.Description;

        var before = role.Permissions.Select(p => p.Permission).Order().ToList();
        db.RolePermissions.RemoveRange(role.Permissions);
        role.Permissions.Clear();
        foreach (var p in Validate(request.Permissions))
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = p });

        audit.Add(AuditAction.PermissionChange, "Role", role.Id.ToString(),
            $"Permissions changed from [{string.Join(", ", before)}] to [{string.Join(", ", request.Permissions.Order())}]");
        await db.SaveChangesAsync(ct);
        return new RoleView(role.Id, role.Name, role.Description, role.IsSystem, role.Permissions.Select(p => p.Permission).Order().ToList());
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role", id);
        if (role.IsSystem) throw new DomainException("System roles cannot be deleted.", "system_role");
        if (await db.UserRoleAssignments.AnyAsync(a => a.RoleId == id, ct))
            throw new DomainException("The role is still assigned to users.", "role_in_use", isConflict: true);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static IEnumerable<string> Validate(IEnumerable<string> permissions)
    {
        var list = permissions.Distinct().ToList();
        var unknown = list.Except(Permissions.All).ToList();
        if (unknown.Count > 0) throw new DomainException($"Unknown permissions: {string.Join(", ", unknown)}", "invalid_permission");
        return list;
    }
}

public record RoleView(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions);
public record RoleRequest(string Name, string? Description, List<string> Permissions);

public sealed class RoleRequestValidator : AbstractValidator<RoleRequest>
{
    public RoleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Permissions).NotNull();
    }
}
