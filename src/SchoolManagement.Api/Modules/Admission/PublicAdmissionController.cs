using Microsoft.AspNetCore.RateLimiting;
using SchoolManagement.Api.Modules.Admission.Services;
using SchoolManagement.Domain.Admission;

namespace SchoolManagement.Api.Modules.Admission;

/// <summary>
/// Anonymous endpoints used by the public website (WEB-02): enquiry form, online application with draft-save,
/// and the offer accept/decline link. Rate-limited and protected by a honeypot field against spam bots.
/// </summary>
[ApiController]
[Route("api/public/admission")]
[Tags("Public: Website")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.PublicForms)]
public sealed class PublicAdmissionController(
    AppDbContext db,
    EnquiryIntake intake,
    ApplicationService applications,
    SelectionService selection,
    AdmissionNotifier notifier) : ControllerBase
{
    /// <summary>Campuses and classes open for admission, for the website's form drop-downs.</summary>
    [HttpGet("options")]
    public async Task<PublicOptions> Options(CancellationToken ct)
    {
        using var _ = db.ElevateToSystem();
        var campuses = await db.Campuses.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code)
            .Select(c => new PublicItem(c.Id, c.Name, c.NameBn)).ToListAsync(ct);
        var classes = await db.YearGroups.AsNoTracking().OrderBy(y => y.Sequence).Select(y => new PublicItem(y.Id, y.Name, y.NameBn)).ToListAsync(ct);
        var years = await db.AcademicYears.AsNoTracking().Where(y => y.Status == Domain.Core.AcademicYearStatus.Open)
            .OrderBy(y => y.StartDate).Select(y => new PublicItem(y.Id, y.Name, null)).ToListAsync(ct);
        return new PublicOptions(campuses, classes, years);
    }

    [HttpPost("enquiries")]
    public async Task<ActionResult<PublicEnquiryResponse>> Enquiry(PublicEnquiryRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.Website)) return Accepted(new PublicEnquiryResponse("received"));
        using var _ = db.ElevateToSystem();
        var e = await intake.CreateAsync(new EnquiryRequest(request.CampusId, EnquirySource.Website, request.CampaignCode, request.AcademicYearId, request.YearGroupId,
            request.ChildName, request.ChildDateOfBirth, request.GuardianName, request.GuardianPhone, request.GuardianEmail, request.Message), ct);
        return Ok(new PublicEnquiryResponse(e.EnquiryNo));
    }

    /// <summary>Starts an online application. Keep the returned access token: it is the only way to resume the draft.</summary>
    [HttpPost("applications")]
    public async Task<PublicApplicationResponse> StartApplication(PublicApplicationRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.Website)) throw new DomainException("Rejected.", "spam");
        using var _ = db.ElevateToSystem();
        var (app, token) = await applications.CreateAsync(request.Application, online: true, ct);
        return new PublicApplicationResponse(app.ApplicationNo, token!, app.Status);
    }

    [HttpGet("applications/{token}")]
    public async Task<ApplicationDetail> GetApplication(string token, CancellationToken ct)
    {
        using var _ = db.ElevateToSystem();
        var app = await applications.FindByTokenAsync(token, ct) ?? throw new NotFoundException("Application", "token");
        return await ApplicationsController.DetailAsync(app, db, ct);
    }

    [HttpPut("applications/{token}")]
    public async Task<ApplicationDetail> SaveDraft(string token, ApplicationRequest request, CancellationToken ct)
    {
        using var _ = db.ElevateToSystem();
        var app = await applications.FindByTokenAsync(token, ct) ?? throw new NotFoundException("Application", "token");
        if (app.Status != ApplicationStatus.Draft) throw new DomainException("A submitted application can only be changed by the admission office.", "not_editable", isConflict: true);
        await applications.UpdateAsync(app, request, ct);
        return await ApplicationsController.DetailAsync(app, db, ct);
    }

    [HttpPost("applications/{token}/documents")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<DocumentView> UploadDocument(string token, [FromForm] string documentType, IFormFile file, CancellationToken ct)
    {
        using var _ = db.ElevateToSystem();
        var app = await applications.FindByTokenAsync(token, ct) ?? throw new NotFoundException("Application", "token");
        if (app.Status != ApplicationStatus.Draft) throw new DomainException("Documents can only be added before submission.", "not_editable", isConflict: true);
        var doc = await applications.AddDocumentAsync(app, file, documentType, ct);
        return new DocumentView(doc.Id, doc.DocumentType, doc.FileName, doc.ContentType, doc.SizeBytes, doc.IsVerified, doc.CreatedAtUtc);
    }

    [HttpPost("applications/{token}/submit")]
    public async Task<ApplicationDetail> Submit(string token, CancellationToken ct)
    {
        using var _ = db.ElevateToSystem();
        var app = await applications.FindByTokenAsync(token, ct) ?? throw new NotFoundException("Application", "token");
        await applications.SubmitAsync(app, ct);
        return await ApplicationsController.DetailAsync(app, db, ct);
    }

    /// <summary>What the guardian sees when opening the link in the offer SMS.</summary>
    [HttpGet("offers/{token}")]
    public async Task<PublicOfferView> GetOffer(string token, CancellationToken ct)
    {
        using var _ = db.ElevateToSystem();
        var offer = await db.Offers.AsNoTracking().Include(o => o.Application).FirstOrDefaultAsync(o => o.ResponseToken == token, ct)
                    ?? throw new NotFoundException("Offer", "token");
        var ctx = await notifier.ContextAsync(offer.Application!, ct);
        return new PublicOfferView(offer.OfferNo, offer.Application!.FullName, ctx.GetValueOrDefault("ClassName"), ctx.GetValueOrDefault("CampusName"),
            ctx.GetValueOrDefault("AcademicYear"), offer.Status, offer.ExpiresAtUtc, offer.FeeScheduleText);
    }

    [HttpGet("offers/{token}/letter")]
    public async Task<ContentResult> OfferLetterPage(string token, [FromQuery] string lang = "bn", CancellationToken ct = default)
    {
        using var _ = db.ElevateToSystem();
        var offer = await db.Offers.AsNoTracking().Include(o => o.Application).ThenInclude(a => a!.Guardians).FirstOrDefaultAsync(o => o.ResponseToken == token, ct)
                    ?? throw new NotFoundException("Offer", "token");
        var ctx = await notifier.ContextAsync(offer.Application!, ct);
        return Content(OfferLetter.Render(offer, offer.Application!, ctx, lang == "bn"), "text/html; charset=utf-8");
    }

    [HttpPost("offers/{token}/respond")]
    public async Task<PublicOfferView> RespondToOffer(string token, OfferResponseRequest request, CancellationToken ct)
    {
        using var _ = db.ElevateToSystem();
        var offer = await db.Offers.Include(o => o.Application).FirstOrDefaultAsync(o => o.ResponseToken == token, ct)
                    ?? throw new NotFoundException("Offer", "token");
        await selection.RespondAsync(offer, request.Accept, request.Note, ct);
        return await GetOffer(token, ct);
    }
}

public record PublicItem(Guid Id, string Name, string? NameBn);
public record PublicOptions(IReadOnlyList<PublicItem> Campuses, IReadOnlyList<PublicItem> Classes, IReadOnlyList<PublicItem> AcademicYears);

/// <param name="Website">Honeypot: hidden on the real form, so only bots fill it in.</param>
public record PublicEnquiryRequest(Guid CampusId, Guid? AcademicYearId, Guid? YearGroupId, string ChildName, DateOnly? ChildDateOfBirth, string GuardianName,
    string GuardianPhone, string? GuardianEmail, string? Message, string? CampaignCode, string? Website);
public record PublicEnquiryResponse(string Reference);
public record PublicApplicationRequest(ApplicationRequest Application, string? Website);
public record PublicApplicationResponse(string ApplicationNo, string AccessToken, ApplicationStatus Status);
public record PublicOfferView(string OfferNo, string ApplicantName, string? ClassName, string? CampusName, string? AcademicYear, OfferStatus Status, DateTime ExpiresAtUtc, string? FeeScheduleText);

public sealed class PublicEnquiryRequestValidator : AbstractValidator<PublicEnquiryRequest>
{
    public PublicEnquiryRequestValidator()
    {
        RuleFor(x => x.ChildName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GuardianName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GuardianPhone).Must(p => Students.Services.StudentImportService.NormalisePhone(p) is not null)
            .WithMessage("Enter a valid Bangladeshi mobile number (01XXXXXXXXX).");
        RuleFor(x => x.GuardianEmail).EmailAddress().When(x => !string.IsNullOrEmpty(x.GuardianEmail));
        RuleFor(x => x.Message).MaximumLength(2000);
    }
}

public sealed class PublicApplicationRequestValidator : AbstractValidator<PublicApplicationRequest>
{
    public PublicApplicationRequestValidator(TimeProvider clock) =>
        RuleFor(x => x.Application).NotNull().SetValidator(new ApplicationRequestValidator(clock));
}
