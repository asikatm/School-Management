using System.Security.Claims;
using SchoolManagement.Infrastructure.Security;

namespace SchoolManagement.Api.Auth;

public static class AppClaims
{
    public const string Permission = "perm";
    public const string Campus = "campus";
    public const string AllCampuses = "all_campus";
    public const string Language = "lang";
    public const string SecurityStamp = "stamp";

    /// <summary>Wildcard campus in a permission claim ("students.view@*") = organisation-wide grant.</summary>
    public const string AnyCampus = "*";
}

/// <summary>Reads the caller from the JWT. Outside an HTTP request (background jobs) it acts as the system user.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    private bool HasHttpContext => accessor.HttpContext is not null;

    private HashSet<string>? _grants;
    private HashSet<string> Grants => _grants ??= Principal?.FindAll(AppClaims.Permission).Select(c => c.Value).ToHashSet() ?? [];

    private List<Guid>? _campusIds;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name);
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public bool IsSystem => !HasHttpContext;
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string Language
    {
        get
        {
            var claim = Principal?.FindFirstValue(AppClaims.Language);
            if (!string.IsNullOrEmpty(claim)) return claim;
            var header = accessor.HttpContext?.Request.Headers.AcceptLanguage.ToString();
            return header?.StartsWith("bn", StringComparison.OrdinalIgnoreCase) == true ? "bn" : "en";
        }
    }

    public bool HasAllCampusAccess => IsSystem || Principal?.FindFirstValue(AppClaims.AllCampuses) == "true";

    public IReadOnlyCollection<Guid> CampusIds => _campusIds ??=
        Principal?.FindAll(AppClaims.Campus).Select(c => Guid.TryParse(c.Value, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).ToList() ?? [];

    public bool HasPermission(string permission) =>
        IsSystem || Grants.Any(g => g.StartsWith(permission + "@", StringComparison.Ordinal));

    public bool HasPermission(string permission, Guid campusId) =>
        IsSystem || Grants.Contains($"{permission}@{AppClaims.AnyCampus}") || Grants.Contains($"{permission}@{campusId}");
}
