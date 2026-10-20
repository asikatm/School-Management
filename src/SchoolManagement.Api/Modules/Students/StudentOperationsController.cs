using System.Net;
using SchoolManagement.Api.Modules.Students.Services;
using SchoolManagement.Domain.Audit;
using SchoolManagement.Domain.Students;
using SchoolManagement.Infrastructure.Auditing;
using SchoolManagement.Infrastructure.Documents;

namespace SchoolManagement.Api.Modules.Students;

/// <summary>SIS-04 allocation &amp; rolls, SIS-05 promotion, SIS-06 withdrawal &amp; TC, SIS-08 ID cards, CORE-10 import/export, guardian portal accounts.</summary>
[ApiController]
[Route("api/students")]
[Tags("Students: Operations")]
public sealed class StudentOperationsController(
    AppDbContext db,
    ISectionAllocator allocator,
    IPromotionService promotion,
    WithdrawalService withdrawals,
    IdCardService idCards,
    StudentImportService importer,
    IGuardianAccountService guardianAccounts,
    IQrCodeService qr,
    IAuditLog audit,
    ICurrentUser currentUser) : ControllerBase
{
    private const string CertificateStyle =
        "body{font-family:'Segoe UI','Noto Sans Bengali',sans-serif;max-width:720px;margin:32px auto;line-height:1.7}" +
        "h1{text-align:center;color:#1e3a5f}td{padding:4px 12px 4px 0}";

    // ---- SIS-04

    /// <summary>Allocates students to sections (balanced by count and optionally gender, or alphabetical). Use dryRun to preview.</summary>
    [HttpPost("allocation")]
    [HasPermission(Permissions.Students.Edit)]
    public Task<BulkAllocationResult> Allocate(BulkAllocationRequest request, CancellationToken ct) => allocator.BulkAllocateAsync(request, ct);

    [HttpPost("roll-numbers")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<int> GenerateRolls(RollGenerationRequest request, CancellationToken ct) => await allocator.GenerateRollsAsync(request, ct);

    // ---- SIS-05

    /// <summary>The review screen: each active student with the suggested outcome (promote / graduate) and target class.</summary>
    [HttpGet("promotion/preview")]
    [HasPermission(Permissions.Students.Approve)]
    public Task<List<PromotionCandidate>> PromotionPreview([FromQuery] Guid academicYearId, [FromQuery] Guid campusId, [FromQuery] Guid? yearGroupId, CancellationToken ct)
    {
        if (!currentUser.CanAccessCampus(campusId)) throw new ForbiddenException();
        return promotion.PreviewAsync(academicYearId, campusId, yearGroupId, ct);
    }

    [HttpPost("promotion/apply")]
    [HasPermission(Permissions.Students.Approve)]
    public Task<PromotionApplyResult> PromotionApply(PromotionApplyRequest request, CancellationToken ct) => promotion.ApplyAsync(request, ct);

    /// <summary>Converts a graduate into an alumni record (the alumni directory, SIS-10, is a later phase).</summary>
    [HttpPost("{id:guid}/alumni")]
    [HasPermission(Permissions.Students.Approve)]
    public async Task<IActionResult> ConvertToAlumni(Guid id, CancellationToken ct)
    {
        var s = await db.Students.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        currentUser.EnsurePermission(Permissions.Students.Approve, s.CampusId);
        if (s.Status != StudentStatus.Graduated) throw new DomainException("Only graduates can become alumni.", "not_graduated", isConflict: true);
        s.Status = StudentStatus.Alumni;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- SIS-06

    [HttpPost("{id:guid}/withdrawals")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<WithdrawalView> RequestWithdrawal(Guid id, WithdrawalCreateRequest request, CancellationToken ct) =>
        ToView(await withdrawals.CreateAsync(id, request, ct));

    [HttpGet("withdrawals")]
    [HasPermission(Permissions.Students.View)]
    public async Task<List<WithdrawalView>> Withdrawals([FromQuery] WithdrawalStatus? status, CancellationToken ct) =>
        (await db.WithdrawalRequests.AsNoTracking().Include(w => w.Student).Where(w => status == null || w.Status == status)
            .OrderByDescending(w => w.CreatedAtUtc).Take(500).ToListAsync(ct)).Select(ToView).ToList();

    [HttpPost("withdrawals/{withdrawalId:guid}/recheck")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<WithdrawalView> RecheckClearance(Guid withdrawalId, CancellationToken ct) => ToView(await withdrawals.RecheckAsync(withdrawalId, ct));

    /// <summary>Issues the transfer certificate once approved and cleared; closes the record and frees the seat.</summary>
    [HttpPost("withdrawals/{withdrawalId:guid}/complete")]
    [HasPermission(Permissions.Students.Approve)]
    public async Task<TransferCertificateView> CompleteWithdrawal(Guid withdrawalId, WithdrawalCompleteRequest request, CancellationToken ct) =>
        ToView(await withdrawals.CompleteAsync(withdrawalId, request, ct));

    /// <summary>TC register (SIS-06).</summary>
    [HttpGet("transfer-certificates")]
    [HasPermission(Permissions.Students.View)]
    public async Task<List<TransferCertificateView>> TransferCertificates([FromQuery] Guid? campusId, CancellationToken ct) =>
        (await db.TransferCertificates.AsNoTracking().Where(t => campusId == null || t.CampusId == campusId).OrderByDescending(t => t.IssueDate).Take(1000).ToListAsync(ct))
        .Select(ToView).ToList();

    [HttpGet("transfer-certificates/{tcId:guid}/print")]
    [HasPermission(Permissions.Students.View)]
    public async Task<ContentResult> PrintTransferCertificate(Guid tcId, CancellationToken ct)
    {
        var tc = await db.TransferCertificates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tcId, ct) ?? throw new NotFoundException("Transfer certificate", tcId);
        var student = await db.Students.AsNoTracking().IgnoreQueryFilters().FirstAsync(s => s.Id == tc.StudentId, ct);
        var school = await db.Organisations.Select(o => o.Name).FirstAsync(ct);
        var campus = await db.Campuses.Where(c => c.Id == tc.CampusId).Select(c => c.Name).FirstAsync(ct);
        var qrPng = Convert.ToBase64String(qr.Png($"TC:{tc.CertificateNo}:{tc.VerificationCode}", 4));
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        var html = $"""
            <!doctype html><html><head><meta charset="utf-8"><title>{E(tc.CertificateNo)}</title>
            <style>{CertificateStyle}</style></head>
            <body><h1>{E(school)}</h1><h3 style="text-align:center">{E(campus)} — Transfer Certificate</h3>
            <p>Certificate no: <b>{E(tc.CertificateNo)}</b> &nbsp; Date: {tc.IssueDate:dd MMMM yyyy}</p>
            <table><tr><td>Name</td><td><b>{E(tc.StudentName)}</b></td></tr><tr><td>Admission no</td><td>{E(student.AdmissionNo)}</td></tr>
            <tr><td>Date of birth</td><td>{student.DateOfBirth:dd MMMM yyyy}</td></tr><tr><td>Date of admission</td><td>{student.AdmissionDate:dd MMMM yyyy}</td></tr>
            <tr><td>Last class attended</td><td>{E(tc.LastClass)}</td></tr><tr><td>Date of leaving</td><td>{student.LeftOn:dd MMMM yyyy}</td></tr>
            <tr><td>Conduct</td><td>{E(tc.Conduct)}</td></tr><tr><td>Remarks</td><td>{E(tc.Remarks)}</td></tr></table>
            <p>All dues have been cleared.</p>
            <p style="margin-top:64px">Principal</p>
            <p><img alt="QR" src="data:image/png;base64,{qrPng}" style="width:90px"/><br/><small>Verification code {E(tc.VerificationCode)}</small></p>
            </body></html>
            """;
        return Content(html, "text/html; charset=utf-8");
    }

    // ---- SIS-08

    /// <summary>Issues new cards for the given students or for a whole section (batch print).</summary>
    [HttpPost("id-cards")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<List<IdCardData>> IssueCards(IssueCardsRequest request, CancellationToken ct)
    {
        var ids = request.StudentIds ?? [];
        if (request.SectionId is { } sid)
            ids.AddRange(await db.StudentEnrolments.Where(e => e.SectionId == sid && e.Status == EnrolmentStatus.Active).Select(e => e.StudentId).ToListAsync(ct));
        if (ids.Count == 0) throw new DomainException("Choose students or a section.", "no_students");
        return await idCards.IssueAsync(ids.Distinct().ToList(), request.ValidUntil, ct);
    }

    [HttpPost("{id:guid}/id-cards/reissue")]
    [HasPermission(Permissions.Students.Edit)]
    public Task<IdCardData> ReissueCard(Guid id, ReasonBody request, CancellationToken ct) => idCards.ReissueAsync(id, request.Reason, ct);

    [HttpGet("id-cards/print")]
    [HasPermission(Permissions.Students.View)]
    public async Task<ContentResult> PrintCards([FromQuery] Guid sectionId, CancellationToken ct)
    {
        var ids = await db.StudentEnrolments.Where(e => e.SectionId == sectionId && e.Status == EnrolmentStatus.Active).Select(e => e.StudentId).ToListAsync(ct);
        var cards = await idCards.CardDataAsync(ids, ct);
        var school = await db.Organisations.Select(o => o.Name).FirstAsync(ct);
        return Content(idCards.RenderHtml(cards, school), "text/html; charset=utf-8");
    }

    [HttpGet("id-cards/{cardNo}/qr")]
    [HasPermission(Permissions.Students.View)]
    public async Task<IActionResult> CardQr(string cardNo, CancellationToken ct)
    {
        var card = await db.IdCards.AsNoTracking().Where(c => c.CardNo == cardNo)
            .Join(db.Students, c => c.StudentId, s => s.Id, (c, s) => new { c.CardNo, s.AdmissionNo }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("ID card", cardNo);
        return File(qr.Png(IdCardService.QrPayload(card.CardNo, card.AdmissionNo)), "image/png");
    }

    // ---- Guardian portal

    [HttpPost("guardians/{guardianId:guid}/portal-account")]
    [HasPermission(Permissions.Students.Edit)]
    public async Task<PortalAccountResult> CreatePortalAccount(Guid guardianId, CancellationToken ct)
    {
        var guardian = await db.Guardians.FirstOrDefaultAsync(g => g.Id == guardianId, ct) ?? throw new NotFoundException("Guardian", guardianId);
        var campusIds = await db.StudentGuardians.Where(sg => sg.GuardianId == guardianId)
            .Join(db.Students, sg => sg.StudentId, s => s.Id, (sg, s) => s.CampusId).Distinct().ToListAsync(ct);
        if (campusIds.Count == 0) throw new DomainException("The guardian is not linked to any student you can access.", "no_students");

        string userName = "";
        string? password = null;
        foreach (var campusId in campusIds)
        {
            currentUser.EnsurePermission(Permissions.Students.Edit, campusId);
            var (u, p) = await guardianAccounts.EnsureAccountAsync(guardian, campusId, ct);
            userName = u;
            password ??= p;
        }
        await db.SaveChangesAsync(ct);
        return new PortalAccountResult(userName, password);
    }

    // ---- CORE-10

    [HttpGet("import/template")]
    [HasPermission(Permissions.Students.Create)]
    public FileContentResult ImportTemplate() =>
        File(importer.Template(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "student_import_template.xlsx");

    /// <summary>Validates every row (dry run by default). With dryRun=false, imports only when every row is valid.</summary>
    [HttpPost("import")]
    [HasPermission(Permissions.Students.Create)]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ImportResult> Import([FromQuery] Guid academicYearId, [FromQuery] bool dryRun, IFormFile file, CancellationToken ct)
    {
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) throw new DomainException("Upload the .xlsx template.", "file_type");
        await using var stream = file.OpenReadStream();
        var result = await importer.ImportAsync(stream, academicYearId, dryRun, ct);
        if (!result.DryRun)
        {
            audit.Add(AuditAction.Custom, "StudentImport", null, $"Imported {result.ImportedRows} students from {file.FileName}");
            await db.SaveChangesAsync(ct);
        }
        return result;
    }

    /// <summary>Exports the filtered student list. Honours campus scope; every export is written to the audit log.</summary>
    [HttpGet("export")]
    [HasPermission(Permissions.Students.Export)]
    public async Task<FileContentResult> Export([FromQuery] StudentQuery query, [FromQuery] string format = "xlsx", CancellationToken ct = default)
    {
        var campuses = await db.Campuses.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var rows = (await StudentQueries.Filter(db, query).OrderBy(r => r.ClassSequence).ThenBy(r => r.Section).ThenBy(r => r.RollNo).Take(50_000).ToListAsync(ct))
            .Select(r => new StudentExportRow(r.AdmissionNo, r.FirstName, r.LastName, r.NameBn, r.DateOfBirth, r.Gender, r.BloodGroup,
                campuses.GetValueOrDefault(r.CampusId, "?"), r.ClassName, r.Section, r.RollNo, r.Status, r.GuardianName, r.GuardianPhone, r.GuardianEmail))
            .ToList();

        audit.Add(AuditAction.Export, nameof(Student), null, $"Exported {rows.Count} students as {format}", query.CampusId);
        await db.SaveChangesAsync(ct);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmm");
        return format.Equals("csv", StringComparison.OrdinalIgnoreCase)
            ? File(StudentImportService.ExportCsv(rows), "text/csv", $"students_{stamp}.csv")
            : File(importer.ExportXlsx(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"students_{stamp}.xlsx");
    }

    private static WithdrawalView ToView(WithdrawalRequest w) => new(w.Id, w.StudentId, w.Student?.AdmissionNo, w.Student?.FullName, w.CampusId,
        w.LastAttendanceDate, w.Reason, w.DestinationSchool, w.Status,
        System.Text.Json.JsonSerializer.Deserialize<List<ClearanceResult>>(w.ClearanceJson ?? "[]") ?? [], w.ApprovalRequestId, w.TransferCertificateId, w.CreatedAtUtc);

    private static TransferCertificateView ToView(TransferCertificate t) =>
        new(t.Id, t.CertificateNo, t.StudentId, t.StudentName, t.LastClass, t.IssueDate, t.Conduct, t.Remarks, t.VerificationCode);
}

public record WithdrawalView(Guid Id, Guid StudentId, string? AdmissionNo, string? StudentName, Guid CampusId, DateOnly LastAttendanceDate, string Reason,
    string? DestinationSchool, WithdrawalStatus Status, IReadOnlyList<ClearanceResult> Clearance, Guid? ApprovalRequestId, Guid? TransferCertificateId, DateTime CreatedAtUtc);
public record TransferCertificateView(Guid Id, string CertificateNo, Guid StudentId, string StudentName, string LastClass, DateOnly IssueDate, string? Conduct,
    string? Remarks, string VerificationCode);
public record IssueCardsRequest(List<Guid>? StudentIds, Guid? SectionId, DateOnly ValidUntil);
public record ReasonBody(string Reason);
public record PortalAccountResult(string UserName, string? TemporaryPassword);

public sealed class WithdrawalCreateRequestValidator : AbstractValidator<WithdrawalCreateRequest>
{
    public WithdrawalCreateRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public sealed class PromotionApplyRequestValidator : AbstractValidator<PromotionApplyRequest>
{
    public PromotionApplyRequestValidator()
    {
        RuleFor(x => x.Decisions).NotEmpty();
        RuleFor(x => x.Decisions).Must(d => d.Select(x => x.StudentId).Distinct().Count() == d.Count).WithMessage("A student appears more than once.");
    }
}

public sealed class ReasonBodyValidator : AbstractValidator<ReasonBody>
{
    public ReasonBodyValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
}
