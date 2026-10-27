using SchoolManagement.Api.Modules.Admission;
using SchoolManagement.Domain.Learning;

namespace SchoolManagement.Api.Modules.Learning;

/// <summary>LRN-02: homework and assignments — set, hand in (portal), grade.</summary>
[ApiController]
[Route("api/learning/homework")]
[Tags("Learning: Homework")]
public sealed class HomeworkController(HomeworkService homework) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Learning.View)]
    public Task<List<AssignmentView>> List([FromQuery] Guid? sectionId, [FromQuery] Guid? subjectId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] bool mine = false, CancellationToken ct = default) => homework.ListAsync(sectionId, subjectId, from, to, mine, ct);

    [HttpPost]
    [HasPermission(Permissions.Learning.Teach)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<AssignmentView> Create([FromForm] AssignmentForm form, CancellationToken ct) => homework.CreateAsync(form, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Learning.View)]
    public Task<AssignmentDetail> Detail(Guid id, CancellationToken ct) => homework.DetailAsync(id, ct);

    [HttpPost("{id:guid}")]
    [HasPermission(Permissions.Learning.Teach)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<AssignmentView> Update(Guid id, [FromForm] AssignmentForm form, CancellationToken ct) => homework.UpdateAsync(id, form, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Learning.Teach)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await homework.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Marks and feedback (also for work checked in class without an online hand-in).</summary>
    [HttpPost("{id:guid}/grades")]
    [HasPermission(Permissions.Learning.Teach)]
    public Task<AssignmentDetail> Grade(Guid id, GradeRequest request, CancellationToken ct) => homework.GradeAsync(id, request, ct);

    [HttpGet("{id:guid}/attachment")]
    [HasPermission(Permissions.Learning.View)]
    public async Task<IActionResult> Attachment(Guid id, CancellationToken ct)
    {
        var (content, type, name) = await homework.AttachmentAsync(id, ct);
        return File(content, type, name);
    }

    [HttpGet("{id:guid}/submissions/{studentId:guid}/file")]
    [HasPermission(Permissions.Learning.View)]
    public async Task<IActionResult> SubmissionFile(Guid id, Guid studentId, CancellationToken ct)
    {
        var (content, type, name) = await homework.SubmissionFileAsync(id, studentId, ct);
        return File(content, type, name);
    }
}

/// <summary>LRN-03: class diary and the daily digest.</summary>
[ApiController]
[Route("api/learning/diary")]
[Tags("Learning: Class diary")]
public sealed class DiaryController(DiaryService diary, Academics.AcademicLookups lookups) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Learning.View)]
    public Task<DailyDigest> Digest([FromQuery] Guid sectionId, [FromQuery] DateOnly? date, CancellationToken ct) =>
        diary.DigestAsync(sectionId, date ?? lookups.TodayDhaka, ct);

    [HttpPost]
    [HasPermission(Permissions.Learning.Teach)]
    public Task<DiaryEntryView> Create(DiaryEntryRequest request, CancellationToken ct) => diary.CreateAsync(request, ct);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Learning.Teach)]
    public Task<DiaryEntryView> Update(Guid id, DiaryEntryRequest request, CancellationToken ct) => diary.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Learning.Teach)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await diary.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>LRN-04: learning resources with access control and download tracking.</summary>
[ApiController]
[Route("api/learning/resources")]
[Tags("Learning: Resources")]
public sealed class ResourcesController(ResourceService resources) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Learning.View)]
    public Task<List<ResourceView>> List([FromQuery] Guid? subjectId, [FromQuery] Guid? yearGroupId, [FromQuery] Guid? sectionId, [FromQuery] ResourceType? type,
        [FromQuery] string? search, CancellationToken ct) => resources.ListAsync(subjectId, yearGroupId, sectionId, type, search, ct);

    [HttpPost]
    [HasPermission(Permissions.Learning.Teach)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<ResourceView> Create([FromForm] ResourceForm form, CancellationToken ct) => resources.CreateAsync(form, ct);

    [HttpPost("{id:guid}")]
    [HasPermission(Permissions.Learning.Teach)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<ResourceView> Update(Guid id, [FromForm] ResourceForm form, CancellationToken ct) => resources.UpdateAsync(id, form, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Learning.Teach)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await resources.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/file")]
    [HasPermission(Permissions.Learning.View)]
    public async Task<IActionResult> Open(Guid id, CancellationToken ct)
    {
        var (content, type, name) = await resources.OpenAsync(id, null, ct);
        return File(content, type, name);
    }

    [HttpPost("{id:guid}/visit")]
    [HasPermission(Permissions.Learning.View)]
    public Task<ResourceLink> Visit(Guid id, CancellationToken ct) => resources.VisitAsync(id, null, ct);

    [HttpGet("{id:guid}/downloads")]
    [HasPermission(Permissions.Learning.View)]
    public Task<List<ResourceDownloadView>> Downloads(Guid id, CancellationToken ct) => resources.DownloadsAsync(id, ct);
}

/// <summary>LRN-08: gradebook for continuous assessment, with moderation by the head of department.</summary>
[ApiController]
[Route("api/learning/gradebook")]
[Tags("Learning: Gradebook")]
public sealed class GradebookController(GradebookService gradebook) : ControllerBase
{
    [HttpGet("categories")]
    [HasPermission(Permissions.Learning.View)]
    public Task<List<CategoryView>> Categories([FromQuery] Guid? yearGroupId, CancellationToken ct) => gradebook.CategoriesAsync(yearGroupId, ct);

    [HttpPut("categories")]
    [HasPermission(Permissions.Learning.Manage)]
    public Task<List<CategoryView>> SaveCategories(CategoriesRequest request, CancellationToken ct) => gradebook.SaveCategoriesAsync(request, ct);

    [HttpGet]
    [HasPermission(Permissions.Learning.View)]
    public Task<GradebookView> Get([FromQuery] Guid sectionId, [FromQuery] Guid subjectId, [FromQuery] Guid termId, CancellationToken ct) =>
        gradebook.GetAsync(sectionId, subjectId, termId, ct);

    [HttpPost("items")]
    [HasPermission(Permissions.Learning.Teach)]
    public Task<GradebookItemView> AddItem(GradebookItemRequest request, CancellationToken ct) => gradebook.AddItemAsync(request, ct);

    [HttpDelete("items/{id:guid}")]
    [HasPermission(Permissions.Learning.Teach)]
    public async Task<IActionResult> DeleteItem(Guid id, CancellationToken ct)
    {
        await gradebook.DeleteItemAsync(id, ct);
        return NoContent();
    }

    [HttpPost("marks")]
    [HasPermission(Permissions.Learning.Teach)]
    public Task<GradebookView> SaveMarks([FromQuery] Guid sectionId, [FromQuery] Guid subjectId, [FromQuery] Guid termId, SaveGradebookMarksRequest request,
        CancellationToken ct) => gradebook.SaveMarksAsync(sectionId, subjectId, termId, request, ct);

    [HttpPost("submit")]
    [HasPermission(Permissions.Learning.Teach)]
    public Task<GradebookView> Submit([FromQuery] Guid sectionId, [FromQuery] Guid subjectId, [FromQuery] Guid termId, CancellationToken ct) =>
        gradebook.SubmitAsync(sectionId, subjectId, termId, ct);

    [HttpPost("moderate")]
    [HasPermission(Permissions.Learning.Moderate)]
    public Task<GradebookView> Moderate([FromQuery] Guid sectionId, [FromQuery] Guid subjectId, [FromQuery] Guid termId, ModerationDecision request,
        CancellationToken ct) => gradebook.ModerateAsync(sectionId, subjectId, termId, request, ct);

    [HttpPost("reopen")]
    [HasPermission(Permissions.Learning.Moderate)]
    public Task<GradebookView> Reopen([FromQuery] Guid sectionId, [FromQuery] Guid subjectId, [FromQuery] Guid termId, ReasonRequest request, CancellationToken ct) =>
        gradebook.ReopenAsync(sectionId, subjectId, termId, request.Reason, ct);

    [HttpGet("moderation")]
    [HasPermission(Permissions.Learning.View)]
    public Task<List<ModerationQueueRow>> Queue([FromQuery] ModerationStatus? status, [FromQuery] Guid? termId, CancellationToken ct) =>
        gradebook.QueueAsync(status, termId, ct);
}

public sealed class AssignmentFormValidator : AbstractValidator<AssignmentForm>
{
    public AssignmentFormValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Instructions).MaximumLength(4000);
        RuleFor(x => x.MaxMarks).GreaterThan(0).LessThanOrEqualTo(1000).When(x => x.MaxMarks is not null);
        RuleFor(x => x.LatePenaltyPercent).InclusiveBetween(0, 100).When(x => x.LatePenaltyPercent is not null);
    }
}

public sealed class GradeRequestValidator : AbstractValidator<GradeRequest>
{
    public GradeRequestValidator()
    {
        RuleFor(x => x.Entries).NotEmpty();
        RuleForEach(x => x.Entries).ChildRules(e => e.RuleFor(x => x.Feedback).MaximumLength(2000));
    }
}

public sealed class DiaryEntryRequestValidator : AbstractValidator<DiaryEntryRequest>
{
    public DiaryEntryRequestValidator()
    {
        RuleFor(x => x.Topics).MaximumLength(2000);
        RuleFor(x => x.Homework).MaximumLength(2000);
        RuleFor(x => x.Reminders).MaximumLength(1000);
        RuleFor(x => x.KitNotes).MaximumLength(500);
    }
}

public sealed class ResourceFormValidator : AbstractValidator<ResourceForm>
{
    public ResourceFormValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Topic).MaximumLength(200);
        RuleFor(x => x.Url).MaximumLength(1000);
    }
}

public sealed class GradebookItemRequestValidator : AbstractValidator<GradebookItemRequest>
{
    public GradebookItemRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MaxMarks).GreaterThan(0).LessThanOrEqualTo(1000);
    }
}

public sealed class CategoriesRequestValidator : AbstractValidator<CategoriesRequest>
{
    public CategoriesRequestValidator() =>
        RuleForEach(x => x.Categories).ChildRules(c =>
        {
            c.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            c.RuleFor(x => x.WeightPercent).InclusiveBetween(0, 100);
        });
}

public sealed class ModerationDecisionValidator : AbstractValidator<ModerationDecision>
{
    public ModerationDecisionValidator()
    {
        RuleFor(x => x.Adjustment).InclusiveBetween(-20, 20);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
