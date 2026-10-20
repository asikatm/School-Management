using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Notifications;

namespace SchoolManagement.Api.Modules.Core;

/// <summary>CORE-08 notification hub: templates with merge fields, outbox with delivery status and retry, preferences, in-app inbox.</summary>
[ApiController]
[Route("api/notifications")]
[Tags("Core: Notifications")]
public sealed class NotificationsController(AppDbContext db, INotificationService notifications, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    [HttpGet("templates")]
    [HasPermission(Permissions.Notifications.View)]
    public async Task<List<TemplateView>> Templates([FromQuery] string? code, CancellationToken ct) =>
        await db.MessageTemplates.AsNoTracking().Where(t => code == null || t.Code == code)
            .OrderBy(t => t.Code).ThenBy(t => t.Channel).ThenBy(t => t.Language)
            .Select(t => new TemplateView(t.Id, t.Code, t.Channel, t.Language, t.Subject, t.Body, t.Version, t.IsActive)).ToListAsync(ct);

    /// <summary>Creates a template, or a new version when one exists for the same code/channel/language (COM-10).</summary>
    [HttpPut("templates")]
    [HasPermission(Permissions.Notifications.ManageTemplates)]
    public async Task<TemplateView> UpsertTemplate(TemplateRequest request, CancellationToken ct)
    {
        var t = await db.MessageTemplates.FirstOrDefaultAsync(x => x.Code == request.Code && x.Channel == request.Channel && x.Language == request.Language, ct);
        if (t is null)
        {
            t = new MessageTemplate { Code = request.Code, Channel = request.Channel, Language = request.Language, Body = request.Body };
            db.MessageTemplates.Add(t);
        }
        else
        {
            t.Version++;
        }
        t.Subject = request.Subject;
        t.Body = request.Body;
        t.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return new TemplateView(t.Id, t.Code, t.Channel, t.Language, t.Subject, t.Body, t.Version, t.IsActive);
    }

    /// <summary>Renders a template with sample data without sending.</summary>
    [HttpPost("templates/preview")]
    [HasPermission(Permissions.Notifications.View)]
    public async Task<PreviewResponse> Preview(PreviewRequest request, CancellationToken ct)
    {
        var t = await db.MessageTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Code == request.Code && x.Channel == request.Channel && x.Language == request.Language, ct)
                ?? throw new NotFoundException("Template", $"{request.Code}/{request.Channel}/{request.Language}");
        var body = NotificationService.Render(t.Body, request.Data);
        var smsSegments = t.Channel == NotificationChannel.Sms ? SmsSegments(body) : (int?)null;
        return new PreviewResponse(t.Subject is null ? null : NotificationService.Render(t.Subject, request.Data), body, smsSegments);
    }

    [HttpGet("outbox")]
    [HasPermission(Permissions.Notifications.View)]
    public async Task<PagedResult<OutboxView>> Outbox([FromQuery] MessageStatus? status, [FromQuery] NotificationChannel? channel, [FromQuery] string? recipient, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var q = db.OutboundMessages.AsNoTracking();
        if (status is not null) q = q.Where(m => m.Status == status);
        if (channel is not null) q = q.Where(m => m.Channel == channel);
        if (!string.IsNullOrWhiteSpace(recipient)) q = q.Where(m => m.Recipient.Contains(recipient));
        return await q.OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new OutboxView(m.Id, m.Channel, m.Priority, m.TemplateCode, m.Recipient, m.Subject, m.Body, m.Status, m.Attempts, m.CreatedAtUtc, m.SentAtUtc, m.LastError))
            .ToPagedAsync(page, ct);
    }

    /// <summary>Puts failed messages back in the queue.</summary>
    [HttpPost("outbox/retry")]
    [HasPermission(Permissions.Notifications.Send)]
    public async Task<int> RetryFailed([FromBody] List<Guid> ids, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var messages = await db.OutboundMessages.Where(m => ids.Contains(m.Id) && m.Status == MessageStatus.Failed).ToListAsync(ct);
        foreach (var m in messages)
        {
            m.Status = MessageStatus.Queued;
            m.Attempts = 0;
            m.NextAttemptAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
        return messages.Count;
    }

    /// <summary>Sends an ad-hoc message, or a template with merge data, to one or more recipients.</summary>
    [HttpPost("send")]
    [HasPermission(Permissions.Notifications.Send)]
    public async Task<SendResponse> Send(SendRequest request, CancellationToken ct)
    {
        if (request.CampusId is { } campus) currentUser.EnsurePermission(Permissions.Notifications.Send, campus);
        var queued = new List<Guid>();
        foreach (var recipient in request.Recipients.Distinct())
        {
            OutboundMessage? message = request.TemplateCode is not null
                ? await notifications.QueueAsync(new NotificationRequest
                {
                    TemplateCode = request.TemplateCode, Channel = request.Channel, Recipient = recipient, Language = request.Language ?? "en",
                    Data = request.Data ?? new Dictionary<string, string?>(), Priority = request.Priority, CampusId = request.CampusId
                }, ct)
                : notifications.QueueRaw(request.Channel, recipient, request.Body!, request.Subject, request.Priority, request.CampusId);
            if (message is null) throw new DomainException($"No active template '{request.TemplateCode}' for {request.Channel}.", "template_missing");
            queued.Add(message.Id);
        }
        await db.SaveChangesAsync(ct);
        return new SendResponse(queued.Count, queued);
    }

    /// <summary>The caller's in-app notifications.</summary>
    [HttpGet("inbox")]
    [Authorize]
    public async Task<PagedResult<InboxItem>> Inbox([FromQuery] bool unreadOnly, [FromQuery] PageQuery page, CancellationToken ct)
    {
        var q = db.OutboundMessages.AsNoTracking().Where(m => m.Channel == NotificationChannel.InApp && m.RecipientUserId == currentUser.UserId);
        if (unreadOnly) q = q.Where(m => m.ReadAtUtc == null);
        return await q.OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new InboxItem(m.Id, m.Subject, m.Body, m.CreatedAtUtc, m.ReadAtUtc, m.RelatedEntityType, m.RelatedEntityId))
            .ToPagedAsync(page, ct);
    }

    [HttpPost("inbox/{id:guid}/read")]
    [Authorize]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var m = await db.OutboundMessages.FirstOrDefaultAsync(x => x.Id == id && x.RecipientUserId == currentUser.UserId, ct) ?? throw new NotFoundException("Message", id);
        m.ReadAtUtc ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("preferences")]
    [Authorize]
    public async Task<PreferenceView> MyPreferences(CancellationToken ct)
    {
        var p = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == currentUser.UserId, ct) ?? new NotificationPreference();
        return new PreferenceView(p.SmsEnabled, p.EmailEnabled, p.PushEnabled, p.WhatsAppEnabled, p.Language, p.QuietHoursStart, p.QuietHoursEnd);
    }

    [HttpPut("preferences")]
    [Authorize]
    public async Task<PreferenceView> SetMyPreferences(PreferenceView request, CancellationToken ct)
    {
        var p = await db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == currentUser.UserId, ct);
        if (p is null)
        {
            p = new NotificationPreference { UserId = currentUser.UserId };
            db.NotificationPreferences.Add(p);
        }
        (p.SmsEnabled, p.EmailEnabled, p.PushEnabled, p.WhatsAppEnabled, p.Language, p.QuietHoursStart, p.QuietHoursEnd) =
            (request.SmsEnabled, request.EmailEnabled, request.PushEnabled, request.WhatsAppEnabled, request.Language, request.QuietHoursStart, request.QuietHoursEnd);
        await db.SaveChangesAsync(ct);
        return request;
    }

    /// <summary>GSM-7 SMS segments are 160/153 chars; Unicode (Bangla) segments are 70/67 (COM-02 cost estimate).</summary>
    public static int SmsSegments(string text)
    {
        var unicode = text.Any(c => c > 127);
        var (single, multi) = unicode ? (70, 67) : (160, 153);
        return text.Length <= single ? 1 : (int)Math.Ceiling(text.Length / (double)multi);
    }
}

public record TemplateView(Guid Id, string Code, NotificationChannel Channel, string Language, string? Subject, string Body, int Version, bool IsActive);
public record TemplateRequest(string Code, NotificationChannel Channel, string Language, string? Subject, string Body, bool IsActive = true);
public record PreviewRequest(string Code, NotificationChannel Channel, string Language, Dictionary<string, string?> Data);
public record PreviewResponse(string? Subject, string Body, int? SmsSegments);
public record OutboxView(Guid Id, NotificationChannel Channel, MessagePriority Priority, string? TemplateCode, string Recipient, string? Subject, string Body,
    MessageStatus Status, int Attempts, DateTime CreatedAtUtc, DateTime? SentAtUtc, string? LastError);
public record SendRequest(NotificationChannel Channel, List<string> Recipients, string? TemplateCode, Dictionary<string, string?>? Data, string? Language,
    string? Subject, string? Body, MessagePriority Priority = MessagePriority.Normal, Guid? CampusId = null);
public record SendResponse(int Queued, IReadOnlyList<Guid> MessageIds);
public record InboxItem(Guid Id, string? Subject, string Body, DateTime CreatedAtUtc, DateTime? ReadAtUtc, string? RelatedEntityType, string? RelatedEntityId);
public record PreferenceView(bool SmsEnabled, bool EmailEnabled, bool PushEnabled, bool WhatsAppEnabled, string Language, TimeOnly? QuietHoursStart, TimeOnly? QuietHoursEnd);

public sealed class TemplateRequestValidator : AbstractValidator<TemplateRequest>
{
    public TemplateRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Language).Must(l => l is "en" or "bn");
        RuleFor(x => x.Body).NotEmpty();
        RuleFor(x => x.Subject).NotEmpty().When(x => x.Channel == NotificationChannel.Email);
    }
}

public sealed class SendRequestValidator : AbstractValidator<SendRequest>
{
    public SendRequestValidator()
    {
        RuleFor(x => x.Recipients).NotEmpty().Must(r => r.Count <= 5000).WithMessage("At most 5,000 recipients per request.");
        RuleFor(x => x.Body).NotEmpty().When(x => x.TemplateCode is null).WithMessage("Provide a template code or a body.");
    }
}
