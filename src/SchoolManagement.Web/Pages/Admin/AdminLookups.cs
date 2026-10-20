using System.Reflection;
using SchoolManagement.Domain.Approvals;
using SchoolManagement.Web.Models;
using SchoolManagement.Web.Services;
using P = SchoolManagement.Domain.Identity.Permissions;

namespace SchoolManagement.Web.Pages.Admin;

public static partial class AdminLookups
{
    /// <summary>The {{MergeField}} names used in template texts, in order of first use.</summary>
    public static IReadOnlyList<string> MergeFields(params string?[] texts) =>
        texts.Where(t => t is not null).SelectMany(t => MergeField().Matches(t!).Select(m => m.Groups[1].Value)).Distinct().ToList();

    [System.Text.RegularExpressions.GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial System.Text.RegularExpressions.Regex MergeField();

    /// <summary>The request types declared in <see cref="ApprovalTypes"/>.</summary>
    public static IReadOnlyList<string> ApprovalRequestTypes { get; } =
        typeof(ApprovalTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order().ToList();

    /// <summary>"CertificateIssue" → "Certificate issue".</summary>
    public static string Humanize(string pascal) =>
        string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

    public static string FileSize(long? bytes) => bytes switch
    {
        null => "—",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
    };
}

/// <summary>
/// Resolves user ids to names for display, caching per page. Needs users.view; without it (or when the lookup
/// fails) the short id is shown instead.
/// </summary>
public sealed class AdminUserNames(ApiClient api, Session session)
{
    private readonly Dictionary<Guid, string> names = [];

    public string this[Guid? id] => id is null ? "—"
        : id == session.Me?.User.Id ? "You"
        : names.TryGetValue(id.Value, out var name) ? name : id.Value.ToString()[..8];

    public async Task LoadAsync(IEnumerable<Guid?> ids)
    {
        if (!session.Has(P.Users.View)) return;
        foreach (var id in ids.OfType<Guid>().Distinct().Where(i => !names.ContainsKey(i) && i != session.Me?.User.Id))
        {
            try { names[id] = (await api.GetAsync<UserDetail>($"api/users/{id}")).FullName; }
            catch (Exception ex) when (ex is ApiException or HttpRequestException) { names[id] = id.ToString()[..8]; }
        }
    }
}
