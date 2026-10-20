using SchoolManagement.Web.Models;
using SchoolManagement.Web.Services;

namespace SchoolManagement.Web.Pages.Students.Components;

/// <summary>Core lookups used by the student pages (campuses, years, classes, sections).</summary>
public static class StudentLookups
{
    public static Task<List<StudentCampusOption>> CampusesAsync(ApiClient api) => api.GetAsync<List<StudentCampusOption>>("api/campuses");

    public static Task<List<StudentAcademicYearOption>> YearsAsync(ApiClient api) => api.GetAsync<List<StudentAcademicYearOption>>("api/academic-years");

    /// <summary>All classes (year groups) across programmes, in programme then class order.</summary>
    public static async Task<List<StudentYearGroupOption>> YearGroupsAsync(ApiClient api)
    {
        var programmes = await api.GetAsync<List<StudentProgrammeOption>>("api/structure/programmes");
        return programmes.OrderBy(p => p.Sequence).SelectMany(p => p.YearGroups.OrderBy(y => y.Sequence)).ToList();
    }

    /// <summary>Sections; without a year the API returns the current year's sections.</summary>
    public static Task<List<StudentSectionOption>> SectionsAsync(ApiClient api, Guid? campusId, Guid? academicYearId, Guid? yearGroupId) =>
        api.GetAsync<List<StudentSectionOption>>(ApiClient.Query("api/structure/sections",
            ("campusId", campusId), ("academicYearId", academicYearId), ("yearGroupId", yearGroupId)));

    public static string SectionLabel(StudentSectionOption s) => $"{s.YearGroupName} - {s.Name} ({s.StudentCount}/{s.Capacity})";
}
