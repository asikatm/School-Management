using SchoolManagement.Domain.Core;
using SchoolManagement.Web.Models;
using SchoolManagement.Web.Services;
using P = SchoolManagement.Domain.Identity.Permissions;

namespace SchoolManagement.Web.Pages.Admission;

/// <summary>
/// Campuses, academic years, classes and staff users for the admission screens. Loaded once per signed-in user and
/// kept for the session, since this master data rarely changes while someone works through admissions.
/// </summary>
public sealed class AdmissionLookups
{
    private static AdmissionLookups? cached;
    private static Guid? cachedFor;

    public static AdmissionLookups Empty { get; } = new();

    public IReadOnlyList<AdmissionCampusOption> Campuses { get; private init; } = [];
    public IReadOnlyList<AdmissionYearOption> Years { get; private init; } = [];
    public IReadOnlyList<AdmissionClassOption> Classes { get; private init; } = [];

    /// <summary>Staff users for counsellor assignment; empty when the user may not list users.</summary>
    public IReadOnlyList<UserListItem> Users { get; private init; } = [];

    public static async Task<AdmissionLookups> LoadAsync(ApiClient api, Session session)
    {
        var userId = session.Me?.User.Id;
        if (cached is not null && cachedFor == userId) return cached;

        var campuses = api.GetAsync<List<AdmissionCampusOption>>("api/campuses");
        var years = api.GetAsync<List<AdmissionYearOption>>("api/academic-years");
        var programmes = api.GetAsync<List<AdmissionProgrammeOption>>("api/structure/programmes");
        Task<PagedResult<UserListItem>>? users = session.Has(P.Users.View)
            ? api.GetAsync<PagedResult<UserListItem>>(ApiClient.Query("api/users", ("type", "Staff"), ("pageSize", 200)))
            : null;
        await Task.WhenAll(campuses, years, programmes);

        IReadOnlyList<UserListItem> staff = [];
        if (users is not null)
        {
            try { staff = (await users).Items.Where(u => u.IsActive).OrderBy(u => u.FullName).ToList(); }
            catch (ApiException) { /* assignment then falls back to "assign to me" */ }
        }

        cached = new AdmissionLookups
        {
            Campuses = (await campuses).Where(c => c.IsActive).ToList(),
            Years = await years,
            Classes = (await programmes).OrderBy(p => p.Sequence).SelectMany(p => p.YearGroups.OrderBy(y => y.Sequence)).ToList(),
            Users = staff
        };
        cachedFor = userId;
        return cached;
    }

    public string Campus(Guid? id) => Campuses.FirstOrDefault(c => c.Id == id)?.Name ?? "—";
    public string Year(Guid? id) => Years.FirstOrDefault(y => y.Id == id)?.Name ?? "—";
    public string Class(Guid? id) => Classes.FirstOrDefault(c => c.Id == id)?.Name ?? "—";

    public string User(Guid? id, Guid? me = null) =>
        id is null ? "Unassigned"
        : id == me ? "Me"
        : Users.FirstOrDefault(u => u.Id == id)?.FullName ?? "Assigned";

    public Guid? DefaultCampusId => Campuses.FirstOrDefault()?.Id;
    public Guid? CurrentYearId => (Years.FirstOrDefault(y => y.IsCurrent) ?? Years.FirstOrDefault(y => y.Status == AcademicYearStatus.Open) ?? Years.FirstOrDefault())?.Id;

    /// <summary>Years that still accept applications (open or being set up).</summary>
    public IEnumerable<AdmissionYearOption> AdmissionYears => Years.Where(y => y.Status is AcademicYearStatus.Open or AcademicYearStatus.Draft);

    public IReadOnlyList<AdmissionOption> CampusOptions => Campuses.Select(c => new AdmissionOption(c.Id, c.Name)).ToList();
    public IReadOnlyList<AdmissionOption> YearOptions => AdmissionYears.Select(y => new AdmissionOption(y.Id, y.Name)).ToList();
    public IReadOnlyList<AdmissionOption> ClassOptions => Classes.Select(c => new AdmissionOption(c.Id, c.Name)).ToList();
}
