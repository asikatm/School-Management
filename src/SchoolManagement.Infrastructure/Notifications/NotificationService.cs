using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Persistence;

namespace SchoolManagement.Infrastructure.Notifications;

public record NotificationRequest
{
    public required string TemplateCode { get; init; }
    public required NotificationChannel Channel { get; init; }
    public required string Recipient { get; init; }
    public IReadOnlyDictionary<string, string?> Data { get; init; } = new Dictionary<string, string?>();
    public string Language { get; init; } = "en";
    public MessagePriority Priority { get; init; } = MessagePriority.Normal;
    public Guid? CampusId { get; init; }
    public Guid? RecipientUserId { get; init; }
    public Guid? RecipientGuardianId { get; init; }
    public string? RelatedEntityType { get; init; }
    public string? RelatedEntityId { get; init; }
}

/// <summary>
/// CORE-08 notification hub. Messages are added to the caller's DbContext and committed with the business
/// change (transactional outbox); <see cref="NotificationDispatchJob"/> delivers them with retry.
/// </summary>
public interface INotificationService
{
    /// <summary>Queues a templated message. Returns null when no active template exists for the code/channel.</summary>
    Task<OutboundMessage?> QueueAsync(NotificationRequest request, CancellationToken ct = default);

    OutboundMessage QueueRaw(NotificationChannel channel, string recipient, string body, string? subject = null,
        MessagePriority priority = MessagePriority.Normal, Guid? campusId = null, Guid? recipientUserId = null);
}

public sealed partial class NotificationService(AppDbContext db, TimeProvider clock) : INotificationService
{
    public async Task<OutboundMessage?> QueueAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var templates = await db.MessageTemplates.AsNoTracking()
            .Where(t => t.Code == request.TemplateCode && t.Channel == request.Channel && t.IsActive)
            .ToListAsync(ct);
        var template = templates.FirstOrDefault(t => t.Language == request.Language)
                       ?? templates.FirstOrDefault(t => t.Language == "en")
                       ?? templates.FirstOrDefault();
        if (template is null) return null;

        var message = new OutboundMessage
        {
            CampusId = request.CampusId,
            Channel = request.Channel,
            Priority = request.Priority,
            TemplateCode = template.Code,
            Recipient = request.Recipient,
            RecipientUserId = request.RecipientUserId,
            RecipientGuardianId = request.RecipientGuardianId,
            Subject = template.Subject is null ? null : Render(template.Subject, request.Data),
            Body = Render(template.Body, request.Data),
            NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime,
            RelatedEntityType = request.RelatedEntityType,
            RelatedEntityId = request.RelatedEntityId
        };
        db.OutboundMessages.Add(message);
        return message;
    }

    public OutboundMessage QueueRaw(NotificationChannel channel, string recipient, string body, string? subject = null,
        MessagePriority priority = MessagePriority.Normal, Guid? campusId = null, Guid? recipientUserId = null)
    {
        var message = new OutboundMessage
        {
            Channel = channel,
            Recipient = recipient,
            Body = body,
            Subject = subject,
            Priority = priority,
            CampusId = campusId,
            RecipientUserId = recipientUserId,
            NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime
        };
        db.OutboundMessages.Add(message);
        return message;
    }

    /// <summary>Replaces {{Field}} merge fields; unknown fields render as empty.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string?> data) =>
        MergeField().Replace(template, m => data.TryGetValue(m.Groups[1].Value, out var v) ? v ?? "" : "");

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex MergeField();
}
