using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Api.Auth;
using SchoolManagement.Domain.Audit;
using SchoolManagement.Infrastructure.Auditing;
using SchoolManagement.Infrastructure.Common;
using SchoolManagement.Infrastructure.Persistence;
using SchoolManagement.Infrastructure.Security;

namespace SchoolManagement.Api.Modules.Identity;

[ApiController]
[Route("api/auth")]
[Tags("Auth")]
public sealed class AuthController(
    AppDbContext db,
    TokenService tokens,
    IPasswordHasher hasher,
    IAuditLog audit,
    ICurrentUser currentUser,
    TimeProvider clock) : ControllerBase
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Sign in with user name, e-mail or phone. Five failures lock the account for 15 minutes (spec 5.5).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var login = request.Login.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == login || u.Email == login || u.Phone == login, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        if (user is not null && user.LockoutEndUtc > now)
            return Problem(statusCode: StatusCodes.Status423Locked, title: "Account is temporarily locked after repeated failed sign-ins. Try again later.");

        if (user is null || !user.IsActive || !hasher.Verify(request.Password, user.PasswordHash))
        {
            if (user is not null)
            {
                user.AccessFailedCount++;
                if (user.AccessFailedCount >= MaxFailedAttempts)
                {
                    user.LockoutEndUtc = now.Add(LockoutDuration);
                    user.AccessFailedCount = 0;
                }
            }
            audit.Add(AuditAction.LoginFailed, "User", user?.Id.ToString(), $"Failed sign-in for '{login}'");
            await db.SaveChangesAsync(ct);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials.");
        }

        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginUtc = now;
        audit.Add(AuditAction.Login, "User", user.Id.ToString(), "Signed in");
        await db.SaveChangesAsync(ct);

        var pair = await tokens.IssueAsync(user, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return new LoginResponse(pair.AccessToken, pair.AccessTokenExpiresAtUtc, pair.RefreshToken, pair.RefreshTokenExpiresAtUtc,
            user.MustChangePassword, new UserSummary(user.Id, user.UserName, user.FullName, user.UserType.ToString(), user.PreferredLanguage));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var result = await tokens.RefreshAsync(request.RefreshToken, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        if (result is not { } r)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Refresh token is invalid or expired.");
        return new LoginResponse(r.Tokens.AccessToken, r.Tokens.AccessTokenExpiresAtUtc, r.Tokens.RefreshToken, r.Tokens.RefreshTokenExpiresAtUtc,
            r.User.MustChangePassword, new UserSummary(r.User.Id, r.User.UserName, r.User.FullName, r.User.UserType.ToString(), r.User.PreferredLanguage));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await tokens.RevokeAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>The signed-in user's profile, campuses and effective permissions.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Include(u => u.RoleAssignments).ThenInclude(a => a.Role)
            .FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct)
            ?? throw new NotFoundException("User", currentUser.UserId!);
        var campusNames = await db.Campuses.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return new MeResponse(
            new UserSummary(user.Id, user.UserName, user.FullName, user.UserType.ToString(), user.PreferredLanguage),
            user.RoleAssignments.Select(a => new RoleScope(a.Role!.Name, a.CampusId,
                a.CampusId is { } c ? campusNames.GetValueOrDefault(c) : "All campuses")).ToList(),
            User.FindAll(AppClaims.Permission).Select(c => c.Value).Order().ToList(),
            user.MustChangePassword);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct)
                   ?? throw new NotFoundException("User", currentUser.UserId!);
        if (!hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new DomainException("Current password is incorrect.", "invalid_password");

        var problems = PasswordPolicy.Validate(request.NewPassword);
        if (problems.Count > 0)
            throw new DomainException(string.Join(" ", problems), "weak_password");

        user.PasswordHash = hasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await tokens.RevokeAllAsync(user.Id, ct);
        return NoContent();
    }
}

public record LoginRequest(string Login, string Password);
public record RefreshRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UserSummary(Guid Id, string UserName, string FullName, string UserType, string Language);
public record LoginResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc, bool MustChangePassword, UserSummary User);
public record RoleScope(string Role, Guid? CampusId, string? CampusName);
public record MeResponse(UserSummary User, IReadOnlyList<RoleScope> Roles, IReadOnlyList<string> Permissions, bool MustChangePassword);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Login).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(PasswordPolicy.MinLength).NotEqual(x => x.CurrentPassword);
    }
}
