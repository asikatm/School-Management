using SchoolManagement.Domain.Identity;

namespace SchoolManagement.Web.Models;

// Mirrors SchoolManagement.Api/Modules/Identity (AuthController, UsersController, RolesController).

public record LoginRequest(string Login, string Password);
public record RefreshRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UserSummary(Guid Id, string UserName, string FullName, string UserType, string Language);
public record LoginResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc, bool MustChangePassword, UserSummary User);
public record RoleScope(string Role, Guid? CampusId, string? CampusName);
public record MeResponse(UserSummary User, IReadOnlyList<RoleScope> Roles, IReadOnlyList<string> Permissions, bool MustChangePassword);

public record UserListItem(Guid Id, string UserName, string FullName, string? Email, string? Phone, UserType UserType, bool IsActive, DateTime? LockoutEndUtc, DateTime? LastLoginUtc);
public record RoleAssignmentView(Guid RoleId, string RoleName, Guid? CampusId);
public record UserDetail(Guid Id, string UserName, string FullName, string? Email, string? Phone, UserType UserType, bool IsActive,
    bool MustChangePassword, string Language, DateTime? LockoutEndUtc, DateTime? LastLoginUtc, IReadOnlyList<RoleAssignmentView> Roles);
public record RoleAssignmentRequest(Guid RoleId, Guid? CampusId);
public record CreateUserRequest(string UserName, string FullName, string? Email, string? Phone, UserType UserType, string? Password, string? Language, List<RoleAssignmentRequest>? Roles);
public record CreateUserResponse(UserDetail User, string? TemporaryPassword);
public record UpdateUserRequest(string FullName, string? Email, string? Phone, string? Language, bool IsActive);
public record ResetPasswordResponse(string TemporaryPassword);

public record RoleView(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions);
public record RoleRequest(string Name, string? Description, List<string> Permissions);
