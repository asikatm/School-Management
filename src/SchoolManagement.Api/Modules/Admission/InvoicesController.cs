using SchoolManagement.Api.Modules.Admission.Services;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Infrastructure.Localization;

namespace SchoolManagement.Api.Modules.Admission;

/// <summary>
/// ADM-07: admission/session invoices raised on acceptance, payable at the cashier. Online gateway payment (FEE-04)
/// and ledger posting arrive with the FEE module in release 2.
/// </summary>
[ApiController]
[Route("api/admission/invoices")]
[Tags("Admission: Offers & Invoices")]
public sealed class InvoicesController(AppDbContext db, AdmissionNotifier notifier, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Admission.View)]
    public async Task<PagedResult<InvoiceView>> List([FromQuery] Guid? campusId, [FromQuery] AdmissionInvoiceStatus? status, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var q = db.AdmissionInvoices.AsNoTracking().Include(i => i.Lines).AsQueryable();
        if (campusId is not null) q = q.Where(i => i.CampusId == campusId);
        if (status is not null) q = q.Where(i => i.Status == status);
        var result = await q.OrderByDescending(i => i.CreatedAtUtc).ToPagedAsync(page, ct);
        return new PagedResult<InvoiceView>(result.Items.Select(ToView).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<InvoiceView> Get(Guid id, CancellationToken ct) =>
        ToView(await db.AdmissionInvoices.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice", id));

    /// <summary>Records a full payment at the counter (cash, cheque, POS, bank deposit, bKash/Nagad reference).</summary>
    [HttpPost("{id:guid}/pay")]
    [HasPermission(Permissions.Admission.Edit)]
    public async Task<InvoiceView> Pay(Guid id, PaymentRequest request, CancellationToken ct)
    {
        var invoice = await db.AdmissionInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice", id);
        currentUser.EnsurePermission(Permissions.Admission.Edit, invoice.CampusId);
        if (invoice.Status != AdmissionInvoiceStatus.Unpaid) throw new DomainException($"The invoice is already {invoice.Status}.", "invoice_closed", isConflict: true);
        if (request.Amount != invoice.Amount)
            throw new DomainException($"The amount must equal the invoice total ({invoice.Amount:N2}); part payments are handled by the FEE module.", "amount_mismatch");

        invoice.Status = AdmissionInvoiceStatus.Paid;
        invoice.PaidAtUtc = clock.GetUtcNow().UtcDateTime;
        invoice.PaymentChannel = request.Channel;
        invoice.PaymentReference = request.Reference;

        var app = await db.Applications.Include(a => a.Guardians).FirstAsync(a => a.Id == invoice.ApplicationId, ct);
        await notifier.NotifyAsync(app, TemplateCodes.PaymentReceived, new Dictionary<string, string?>
        {
            ["Amount"] = BanglaFormat.GroupLakhCrore(invoice.Amount), ["InvoiceNo"] = invoice.InvoiceNo, ["Reference"] = request.Reference
        }, ct);
        await db.SaveChangesAsync(ct);
        return ToView(invoice);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Admission.Approve)]
    public async Task<InvoiceView> Cancel(Guid id, ReasonRequest request, CancellationToken ct)
    {
        var invoice = await db.AdmissionInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice", id);
        currentUser.EnsurePermission(Permissions.Admission.Approve, invoice.CampusId);
        if (invoice.Status != AdmissionInvoiceStatus.Unpaid) throw new DomainException("Only unpaid invoices can be cancelled; paid ones need a refund (FEE-08).", "invoice_closed", isConflict: true);
        invoice.Status = AdmissionInvoiceStatus.Cancelled;
        invoice.PaymentReference = $"Cancelled: {request.Reason}";
        await db.SaveChangesAsync(ct);
        return ToView(invoice);
    }

    internal static InvoiceView ToView(AdmissionInvoice i) => new(i.Id, i.InvoiceNo, i.ApplicationId, i.CampusId, i.DueDate, i.Amount, i.Status, i.PaidAtUtc,
        i.PaymentChannel, i.PaymentReference, i.Lines.Select(l => new FeeLine(l.FeeHeadId, l.Description, l.Amount)).ToList());
}

public record InvoiceView(Guid Id, string InvoiceNo, Guid ApplicationId, Guid CampusId, DateOnly DueDate, decimal Amount, AdmissionInvoiceStatus Status,
    DateTime? PaidAtUtc, string? PaymentChannel, string? PaymentReference, IReadOnlyList<FeeLine> Lines);
public record PaymentRequest(decimal Amount, string Channel, string Reference);

public sealed class PaymentRequestValidator : AbstractValidator<PaymentRequest>
{
    public PaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Channel).NotEmpty().Must(c => c is "Cash" or "Cheque" or "POS" or "BankDeposit" or "bKash" or "Nagad" or "Rocket" or "Card")
            .WithMessage("Channel must be Cash, Cheque, POS, BankDeposit, bKash, Nagad, Rocket or Card.");
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(100);
    }
}
