using System.Net;
using System.Text.Json;
using SchoolManagement.Api.Modules.Admission.Services;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Infrastructure.Localization;

namespace SchoolManagement.Api.Modules.Admission;

/// <summary>ADM-06: offers, bilingual offer letters, recorded responses and revocation.</summary>
[ApiController]
[Route("api/admission/offers")]
[Tags("Admission: Offers & Invoices")]
public sealed class OffersController(AppDbContext db, SelectionService selection, AdmissionNotifier notifier, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Admission.View)]
    public async Task<PagedResult<OfferView>> List([FromQuery] Guid? campusId, [FromQuery] OfferStatus? status, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var q = db.Offers.AsNoTracking().Include(o => o.Application).AsQueryable();
        if (campusId is not null) q = q.Where(o => o.CampusId == campusId);
        if (status is not null) q = q.Where(o => o.Status == status);
        var result = await q.OrderByDescending(o => o.IssuedAtUtc).ToPagedAsync(page, ct);
        return new PagedResult<OfferView>(result.Items.Select(o => ToView(o, o.Application!)).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    /// <summary>Printable offer letter in English or Bangla (HTML; the DOC module adds letterhead and PDF in release 2).</summary>
    [HttpGet("{id:guid}/letter")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<ContentResult> Letter(Guid id, [FromQuery] string lang = "en", CancellationToken ct = default)
    {
        var offer = await db.Offers.AsNoTracking().Include(o => o.Application).ThenInclude(a => a!.Guardians).FirstOrDefaultAsync(o => o.Id == id, ct)
                    ?? throw new NotFoundException("Offer", id);
        var ctx = await notifier.ContextAsync(offer.Application!, ct);
        return Content(OfferLetter.Render(offer, offer.Application!, ctx, lang == "bn"), "text/html; charset=utf-8");
    }

    /// <summary>Records a response received by phone or at the office.</summary>
    [HttpPost("{id:guid}/respond")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<OfferView> Respond(Guid id, OfferResponseRequest request, CancellationToken ct)
    {
        var offer = await db.Offers.Include(o => o.Application).FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("Offer", id);
        await selection.RespondAsync(offer, request.Accept, request.Note, ct);
        return ToView(offer, offer.Application!);
    }

    [HttpPost("{id:guid}/revoke")]
    [HasPermission(Permissions.Admission.Approve)]
    public async Task<OfferView> Revoke(Guid id, ReasonRequest request, CancellationToken ct)
    {
        var offer = await db.Offers.Include(o => o.Application).FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("Offer", id);
        currentUser.EnsurePermission(Permissions.Admission.Approve, offer.CampusId);
        if (offer.Status != OfferStatus.Pending) throw new DomainException($"The offer is already {offer.Status}.", "offer_closed", isConflict: true);
        offer.Status = OfferStatus.Revoked;
        offer.ResponseNote = request.Reason;
        offer.Application!.Status = ApplicationStatus.Shortlisted;
        await db.SaveChangesAsync(ct);
        var app = offer.Application;
        await selection.PromoteWaitlistAsync(new ClassKey(app.CampusId, app.AcademicYearId, app.YearGroupId), ct);
        return ToView(offer, app);
    }

    internal static OfferView ToView(Offer o, Application a) => new(o.Id, o.OfferNo, o.ApplicationId, a.ApplicationNo, a.FullName, o.CampusId, o.Status,
        o.IssuedAtUtc, o.ExpiresAtUtc, o.RespondedAtUtc, o.ResponseNote, o.FeeScheduleText,
        string.IsNullOrEmpty(o.FeeLinesJson) ? [] : JsonSerializer.Deserialize<List<FeeLine>>(o.FeeLinesJson) ?? []);
}

public record OfferView(Guid Id, string OfferNo, Guid ApplicationId, string ApplicationNo, string ApplicantName, Guid CampusId, OfferStatus Status,
    DateTime IssuedAtUtc, DateTime ExpiresAtUtc, DateTime? RespondedAtUtc, string? ResponseNote, string? FeeScheduleText, IReadOnlyList<FeeLine> FeeLines);
public record OfferResponseRequest(bool Accept, string? Note);

internal static class OfferLetter
{
    private const string Style =
        "body{font-family:'Segoe UI','Noto Sans Bengali','SolaimanLipi',sans-serif;max-width:720px;margin:24px auto;line-height:1.6;color:#1b1b1b}" +
        "h1{font-size:18pt;color:#1e3a5f}";

    public static string Render(Offer offer, Application app, IReadOnlyDictionary<string, string?> ctx, bool bangla)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var guardian = app.Guardians.FirstOrDefault(g => g.IsPrimaryContact) ?? app.Guardians.FirstOrDefault();
        var lines = string.IsNullOrEmpty(offer.FeeLinesJson) ? [] : JsonSerializer.Deserialize<List<FeeLine>>(offer.FeeLinesJson) ?? [];
        var issued = BanglaFormat.ToDhaka(offer.IssuedAtUtc).ToString("dd MMMM yyyy");
        var expires = BanglaFormat.ToDhaka(offer.ExpiresAtUtc).ToString("dd MMMM yyyy");
        string N(string s) => bangla ? BanglaFormat.ToBanglaDigits(s) : s;

        var feeRows = string.Concat(lines.Select(l => $"<tr><td>{E(l.Description)}</td><td style=\"text-align:right\">{N(BanglaFormat.FormatBdt(l.Amount))}</td></tr>"));
        var feeTable = lines.Count == 0 ? "" :
            $"<table style=\"border-collapse:collapse;min-width:60%\" border=\"1\" cellpadding=\"6\">{feeRows}<tr><th>{(bangla ? "মোট" : "Total")}</th><th style=\"text-align:right\">{N(BanglaFormat.FormatBdt(lines.Sum(l => l.Amount)))}</th></tr></table>";

        var body = bangla
            ? $"""
              <p>তারিখ: {N(issued)}<br/>অফার নং: {E(offer.OfferNo)}</p>
              <p>প্রিয় {E(guardian?.Name)},</p>
              <p>আনন্দের সাথে জানাচ্ছি যে <b>{E(app.NameBn ?? app.FullName)}</b> (আবেদন নং {E(app.ApplicationNo)})-কে {E(ctx.GetValueOrDefault("AcademicYear"))} শিক্ষাবর্ষে {E(ctx.GetValueOrDefault("CampusName"))} ক্যাম্পাসে <b>{E(ctx.GetValueOrDefault("ClassName"))}</b>-এ ভর্তির প্রস্তাব দেওয়া হলো।</p>
              {feeTable}<p>{E(offer.FeeScheduleText)}</p>
              <p>অনুগ্রহ করে <b>{N(expires)}</b>-এর মধ্যে প্রস্তাবটি গ্রহণ বা প্রত্যাখ্যান করুন। নির্ধারিত সময়ের মধ্যে উত্তর না পেলে আসনটি অপেক্ষমাণ তালিকার পরবর্তী প্রার্থীকে দেওয়া হবে।</p>
              <p>ভর্তি অফিস<br/>{E(ctx.GetValueOrDefault("SchoolName"))}</p>
              """
            : $"""
              <p>Date: {issued}<br/>Offer no: {E(offer.OfferNo)}</p>
              <p>Dear {E(guardian?.Name)},</p>
              <p>We are pleased to offer <b>{E(app.FullName)}</b> (application {E(app.ApplicationNo)}) a place in <b>{E(ctx.GetValueOrDefault("ClassName"))}</b> at our {E(ctx.GetValueOrDefault("CampusName"))} campus for the {E(ctx.GetValueOrDefault("AcademicYear"))} academic year.</p>
              {feeTable}<p>{E(offer.FeeScheduleText)}</p>
              <p>Please accept or decline this offer by <b>{expires}</b>. If we do not hear from you by then, the seat will be offered to the next applicant on the waiting list.</p>
              <p>Admissions Office<br/>{E(ctx.GetValueOrDefault("SchoolName"))}</p>
              """;

        return $"""
                <!doctype html><html lang="{(bangla ? "bn" : "en")}"><head><meta charset="utf-8"><title>{E(offer.OfferNo)}</title>
                <style>{Style}</style>
                </head><body><h1>{E(ctx.GetValueOrDefault("SchoolName"))}</h1><h2>{(bangla ? "ভর্তির প্রস্তাবপত্র" : "Offer of Admission")}</h2>{body}</body></html>
                """;
    }
}
