using Microsoft.AspNetCore.RateLimiting;
using SchoolManagement.Api.Modules.Admission;
using SchoolManagement.Domain.Exams;

namespace SchoolManagement.Api.Modules.Exams;

/// <summary>EXM-01/02: exams, date sheets, clash check, publication, seating, invigilators and admit cards.</summary>
[ApiController]
[Route("api/exams")]
[Tags("Exams: Schedule & seating")]
public sealed class ExamsController(ExamService exams) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Exams.View)]
    public Task<List<ExamView>> List([FromQuery] Guid? academicYearId, [FromQuery] ExamType? type, CancellationToken ct) => exams.ListAsync(academicYearId, type, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Exams.View)]
    public Task<ExamView> Get(Guid id, CancellationToken ct) => exams.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<ExamView> Create(ExamRequest request, CancellationToken ct) => exams.CreateAsync(request, ct);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<ExamView> Update(Guid id, ExamRequest request, CancellationToken ct) => exams.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Exams.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await exams.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/papers")]
    [HasPermission(Permissions.Exams.View)]
    public Task<List<PaperView>> Papers(Guid id, CancellationToken ct) => exams.PapersAsync(id, ct);

    [HttpPost("{id:guid}/papers")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<PaperView> AddPaper(Guid id, PaperRequest request, CancellationToken ct) => exams.AddPaperAsync(id, request, ct);

    [HttpPut("papers/{paperId:guid}")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<PaperView> UpdatePaper(Guid paperId, PaperRequest request, CancellationToken ct) => exams.UpdatePaperAsync(paperId, request, ct);

    [HttpDelete("papers/{paperId:guid}")]
    [HasPermission(Permissions.Exams.Manage)]
    public async Task<IActionResult> DeletePaper(Guid paperId, CancellationToken ct)
    {
        await exams.DeletePaperAsync(paperId, ct);
        return NoContent();
    }

    /// <summary>Overlapping papers of a class that share students (elective combinations included).</summary>
    [HttpGet("{id:guid}/clashes")]
    [HasPermission(Permissions.Exams.View)]
    public Task<List<ExamClash>> Clashes(Guid id, CancellationToken ct) => exams.ClashesAsync(id, ct);

    [HttpGet("{id:guid}/date-sheet")]
    [HasPermission(Permissions.Exams.View)]
    public Task<DateSheet> DateSheet(Guid id, CancellationToken ct) => exams.DateSheetAsync(id, ct);

    /// <summary>Publish (or withdraw) the date sheet to the portal and website; the first publication notifies guardians.</summary>
    [HttpPost("{id:guid}/schedule/publish")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<ExamView> PublishSchedule(Guid id, [FromQuery] bool publish = true, CancellationToken ct = default) => exams.PublishScheduleAsync(id, publish, ct);

    [HttpGet("{id:guid}/seating")]
    [HasPermission(Permissions.Exams.View)]
    public Task<SeatingView> Seating(Guid id, [FromQuery] Guid campusId, CancellationToken ct) => exams.SeatingAsync(id, campusId, ct);

    [HttpPost("{id:guid}/seating")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<SeatingView> GenerateSeating(Guid id, SeatingRequest request, CancellationToken ct) => exams.GenerateSeatingAsync(id, request, ct);

    [HttpPut("{id:guid}/invigilators")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<SeatingView> SetInvigilators(Guid id, InvigilatorRequest request, CancellationToken ct) => exams.SetInvigilatorsAsync(id, request, ct);

    [HttpPost("{id:guid}/invigilators/auto")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<SeatingView> AutoInvigilators(Guid id, [FromQuery] Guid campusId, CancellationToken ct) => exams.AutoInvigilatorsAsync(id, campusId, ct);

    [HttpGet("{id:guid}/seat")]
    [HasPermission(Permissions.Exams.View)]
    public Task<List<SeatLookup>> Seat(Guid id, [FromQuery] string q, CancellationToken ct) => exams.LookupSeatAsync(id, q, ct);

    [HttpGet("{id:guid}/admit-cards")]
    [HasPermission(Permissions.Exams.View)]
    public async Task<ContentResult> AdmitCards(Guid id, [FromQuery] Guid? campusId, [FromQuery] Guid? sectionId, [FromQuery] Guid? studentId, CancellationToken ct) =>
        Content(await exams.AdmitCardsAsync(id, campusId, sectionId, studentId, ct), "text/html; charset=utf-8");
}

/// <summary>EXM-03: mark sheets — entry, verification entry, Excel, gradebook CA, submission, approval and re-open.</summary>
[ApiController]
[Route("api/exams")]
[Tags("Exams: Marks")]
public sealed class MarksController(MarkService marks) : ControllerBase
{
    [HttpGet("my-mark-sheets")]
    [HasPermission(Permissions.Exams.Marks)]
    public Task<List<MyMarkSheet>> Mine(CancellationToken ct) => marks.MineAsync(ct);

    [HttpGet("{id:guid}/mark-sheets")]
    [HasPermission(Permissions.Exams.View)]
    public Task<List<MarkSheetSummary>> Sheets(Guid id, CancellationToken ct) => marks.SheetsAsync(id, ct);

    [HttpGet("papers/{paperId:guid}/sections/{sectionId:guid}/marks")]
    [HasPermission(Permissions.Exams.View)]
    public Task<MarkSheetView> Get(Guid paperId, Guid sectionId, CancellationToken ct) => marks.GetAsync(paperId, sectionId, ct);

    [HttpPost("papers/{paperId:guid}/sections/{sectionId:guid}/marks")]
    [HasPermission(Permissions.Exams.Marks)]
    public Task<MarkSheetView> Save(Guid paperId, Guid sectionId, SaveMarksRequest request, CancellationToken ct) => marks.SaveAsync(paperId, sectionId, request, ct);

    [HttpPost("papers/{paperId:guid}/sections/{sectionId:guid}/gradebook")]
    [HasPermission(Permissions.Exams.Marks)]
    public Task<MarkSheetView> Gradebook(Guid paperId, Guid sectionId, CancellationToken ct) => marks.PullGradebookAsync(paperId, sectionId, ct);

    [HttpPost("papers/{paperId:guid}/sections/{sectionId:guid}/submit")]
    [HasPermission(Permissions.Exams.Marks)]
    public Task<MarkSheetView> Submit(Guid paperId, Guid sectionId, CancellationToken ct) => marks.SubmitAsync(paperId, sectionId, ct);

    [HttpPost("papers/{paperId:guid}/sections/{sectionId:guid}/approve")]
    [HasPermission(Permissions.Exams.Moderate)]
    public Task<MarkSheetView> Approve(Guid paperId, Guid sectionId, CancellationToken ct) => marks.ApproveAsync(paperId, sectionId, ct);

    [HttpPost("papers/{paperId:guid}/sections/{sectionId:guid}/reopen")]
    [HasPermission(Permissions.Exams.Moderate)]
    public Task<MarkSheetView> Reopen(Guid paperId, Guid sectionId, ReasonRequest request, CancellationToken ct) => marks.ReopenAsync(paperId, sectionId, request.Reason, ct);

    [HttpGet("papers/{paperId:guid}/sections/{sectionId:guid}/template")]
    [HasPermission(Permissions.Exams.Marks)]
    public async Task<IActionResult> Template(Guid paperId, Guid sectionId, CancellationToken ct) =>
        File(await marks.TemplateAsync(paperId, sectionId, ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "marks.xlsx");

    /// <summary>Upload marks from the Excel template; run with dryRun=true first to see problems.</summary>
    [HttpPost("papers/{paperId:guid}/sections/{sectionId:guid}/import")]
    [HasPermission(Permissions.Exams.Marks)]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<MarkImportResult> Import(Guid paperId, Guid sectionId, IFormFile file, [FromQuery] bool dryRun = true, [FromQuery] bool secondEntry = false,
        CancellationToken ct = default)
    {
        await using var stream = file.OpenReadStream();
        return await marks.ImportAsync(paperId, sectionId, stream, dryRun, secondEntry, ct);
    }
}

/// <summary>EXM-04/05/06/10: tabulation, finalising, publication with embargo, remarks, report cards, transcripts and analysis.</summary>
[ApiController]
[Route("api/exams")]
[Tags("Exams: Results")]
public sealed class ResultsController(ResultService results) : ControllerBase
{
    [HttpGet("{id:guid}/results")]
    [HasPermission(Permissions.Exams.View)]
    public Task<TabulationView> Tabulation(Guid id, [FromQuery] Guid? sectionId, CancellationToken ct) => results.TabulationAsync(id, sectionId, ct);

    [HttpPost("{id:guid}/results/finalise")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<TabulationView> Finalise(Guid id, CancellationToken ct) => results.FinaliseAsync(id, ct);

    [HttpPost("{id:guid}/results/unfinalise")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<ExamView> Unfinalise(Guid id, CancellationToken ct) => results.UnfinaliseAsync(id, ct);

    /// <summary>Publish now, or at publishAtUtc (embargo).</summary>
    [HttpPost("{id:guid}/results/publish")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<ExamView> Publish(Guid id, PublishRequest request, CancellationToken ct) => results.PublishAsync(id, request, ct);

    [HttpPost("{id:guid}/results/withdraw")]
    [HasPermission(Permissions.Exams.Manage)]
    public Task<ExamView> Withdraw(Guid id, CancellationToken ct) => results.WithdrawAsync(id, ct);

    [HttpGet("{id:guid}/remarks")]
    [HasPermission(Permissions.Exams.View)]
    public Task<RemarksView> Remarks(Guid id, [FromQuery] Guid sectionId, CancellationToken ct) => results.RemarksAsync(id, sectionId, ct);

    [HttpPost("{id:guid}/remarks")]
    [HasPermission(Permissions.Exams.Marks)]
    public Task<RemarksView> SaveRemarks(Guid id, [FromQuery] Guid sectionId, SaveRemarksRequest request, CancellationToken ct) =>
        results.SaveRemarksAsync(id, sectionId, request, ct);

    [HttpGet("{id:guid}/report-cards")]
    [HasPermission(Permissions.Exams.View)]
    public async Task<ContentResult> ReportCards(Guid id, [FromQuery] Guid? sectionId, [FromQuery] Guid? studentId, CancellationToken ct) =>
        Content(await results.ReportCardsAsync(id, sectionId, studentId, $"{Request.Scheme}://{Request.Host}", ct), "text/html; charset=utf-8");

    [HttpGet("{id:guid}/analysis")]
    [HasPermission(Permissions.Exams.View)]
    public Task<ResultAnalysis> Analysis(Guid id, [FromQuery] Guid? compareExamId, CancellationToken ct) => results.AnalysisAsync(id, compareExamId, ct);

    [HttpGet("transcripts/{studentId:guid}")]
    [HasPermission(Permissions.Exams.View)]
    public Task<Transcript> Transcript(Guid studentId, CancellationToken ct) => results.TranscriptAsync(studentId, false, ct);

    [HttpGet("transcripts/{studentId:guid}/print")]
    [HasPermission(Permissions.Exams.View)]
    public async Task<ContentResult> TranscriptPrint(Guid studentId, CancellationToken ct) =>
        Content(await results.TranscriptHtmlAsync(studentId, false, ct), "text/html; charset=utf-8");
}

/// <summary>Website feeds: published date sheets (EXM-01) and the report-card QR check (EXM-05). No sign-in.</summary>
[ApiController]
[Route("api/public")]
[Tags("Public")]
[AllowAnonymous]
public sealed class PublicExamsController(ExamService exams, ResultService results, AppDbContext db) : ControllerBase
{
    [HttpGet("exams/date-sheets")]
    public async Task<List<DateSheet>> DateSheets([FromQuery] Guid? yearGroupId, CancellationToken ct)
    {
        using var _ = db.ElevateToSystem();
        return await exams.PublishedDateSheetsAsync(yearGroupId, ct);
    }

    [HttpGet("verify/{code}")]
    [EnableRateLimiting(RateLimitPolicies.PublicForms)]
    public Task<VerificationView> Verify(string code, CancellationToken ct) => results.VerifyAsync(code, ct);
}

public sealed class ExamRequestValidator : AbstractValidator<ExamRequest>
{
    public ExamRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9-]+$").WithMessage("Use letters, digits and dashes only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameBn).MaximumLength(200);
    }
}

public sealed class PaperRequestValidator : AbstractValidator<PaperRequest>
{
    public PaperRequestValidator() =>
        RuleForEach(x => x.Components).ChildRules(c =>
        {
            c.RuleFor(x => x.Name).NotEmpty().MaximumLength(64);
            c.RuleFor(x => x.MaxMarks).GreaterThan(0).LessThanOrEqualTo(1000);
            c.RuleFor(x => x.WeightPercent).InclusiveBetween(0, 100);
        });
}

public sealed class SaveRemarksRequestValidator : AbstractValidator<SaveRemarksRequest>
{
    public SaveRemarksRequestValidator() =>
        RuleForEach(x => x.Entries).ChildRules(e =>
        {
            e.RuleFor(x => x.TeacherRemark).MaximumLength(1000);
            e.RuleFor(x => x.PrincipalRemark).MaximumLength(1000);
            e.RuleFor(x => x.CoCurricular).MaximumLength(1000);
        });
}
