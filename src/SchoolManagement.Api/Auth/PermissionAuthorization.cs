using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace SchoolManagement.Api.Auth;

/// <summary>
/// Requires the caller to hold <paramref name="permission"/> on at least one campus. Campus-specific checks
/// for writes happen in the services via <c>ICurrentUser.EnsurePermission(permission, campusId)</c>.
/// </summary>
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PolicyPrefix + permission)
{
    public const string PolicyPrefix = "perm:";
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var prefix = requirement.Permission + "@";
        if (context.User.FindAll(AppClaims.Permission).Any(c => c.Value.StartsWith(prefix, StringComparison.Ordinal)))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Builds "perm:xyz" policies on demand so every permission does not need registering up front.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[HasPermissionAttribute.PolicyPrefix.Length..]))
            .Build();
    }
}
