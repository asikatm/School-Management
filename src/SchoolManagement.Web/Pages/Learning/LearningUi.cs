using System.Globalization;
using MudBlazor;
using SchoolManagement.Domain.Learning;
using SchoolManagement.Web.Models;
using SchoolManagement.Web.Services;

namespace SchoolManagement.Web.Pages.Learning;

/// <summary>Shared look-ups, labels and colours for the LRN pages.</summary>
public static class LearningUi
{
    /// <summary>The subjects taught in a class (its curriculum); every subject when the class has no curriculum yet.</summary>
    public static async Task<List<SubjectView>> SubjectsForClassAsync(ApiClient api, Guid? yearGroupId)
    {
        if (yearGroupId is { } yg)
        {
            var curriculum = await api.GetAsync<CurriculumView>($"api/academics/curriculum/{yg}");
            if (curriculum.Subjects.Count > 0)
                return curriculum.Subjects.Select(s => new SubjectView(s.SubjectId, s.Code, s.Name, null, s.Type, null)).ToList();
        }
        return await api.GetAsync<List<SubjectView>>("api/master/subjects");
    }

    public static async Task<List<TermView>> TermsAsync(ApiClient api, Guid? academicYearId)
    {
        var years = await api.GetAsync<List<AcademicYearView>>("api/academic-years");
        var year = years.FirstOrDefault(y => y.Id == academicYearId) ?? years.FirstOrDefault(y => y.IsCurrent) ?? years.FirstOrDefault();
        return year?.Terms.OrderBy(t => t.Sequence).ToList() ?? [];
    }

    public static TermView? CurrentTerm(IReadOnlyList<TermView> terms)
    {
        var today = Today;
        return terms.FirstOrDefault(t => t.StartDate <= today && t.EndDate >= today) ?? terms.LastOrDefault(t => t.StartDate <= today) ?? terms.FirstOrDefault();
    }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6));

    public static DateTime NowLocal => DateTime.UtcNow.AddHours(6);

    public static string Due(DateTime local) => local.ToString("ddd dd MMM, hh:mm tt", CultureInfo.InvariantCulture);

    public static Color Color(SubmissionStatus? s) => s switch
    {
        SubmissionStatus.Graded => MudBlazor.Color.Success,
        SubmissionStatus.ReturnedForRedo => MudBlazor.Color.Warning,
        SubmissionStatus.Submitted => MudBlazor.Color.Info,
        _ => MudBlazor.Color.Default
    };

    public static string Label(SubmissionStatus? s) => s switch
    {
        SubmissionStatus.Graded => "Marked",
        SubmissionStatus.ReturnedForRedo => "Redo",
        SubmissionStatus.Submitted => "Handed in",
        _ => "Not handed in"
    };

    public static Color Color(ModerationStatus s) => s switch
    {
        ModerationStatus.Approved => MudBlazor.Color.Success,
        ModerationStatus.Submitted => MudBlazor.Color.Info,
        ModerationStatus.Returned => MudBlazor.Color.Warning,
        _ => MudBlazor.Color.Default
    };

    public static string Icon(ResourceType t) => t switch
    {
        ResourceType.Notes => Icons.Material.Filled.Description,
        ResourceType.Slides => Icons.Material.Filled.Slideshow,
        ResourceType.Video => Icons.Material.Filled.OndemandVideo,
        ResourceType.PastPaper => Icons.Material.Filled.HistoryEdu,
        ResourceType.Worksheet => Icons.Material.Filled.Assignment,
        ResourceType.Link => Icons.Material.Filled.Link,
        _ => Icons.Material.Filled.AttachFile
    };

    public static string Size(long? bytes) => bytes switch
    {
        null => "",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.0} MB"
    };
}
