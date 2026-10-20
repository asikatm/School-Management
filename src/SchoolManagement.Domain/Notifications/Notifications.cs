using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Notifications;

public enum NotificationChannel
{
    InApp = 0,
    Push = 1,
    Sms = 2,
    Email = 3,
    WhatsApp = 4
}

/// <summary>CORE-08 / COM-10: a template per event, channel and language with {{MergeField}} placeholders.</summary>
[Audited]
public class MessageTemplate : SoftDeleteEntity
{
    public required string Code { get; set; }
    public NotificationChannel Channel { get; set; }
    public string Language { get; set; } = "en";
    public string? Subject { get; set; }
    public required string Body { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public enum MessageStatus
{
    Queued = 0,
    Sending = 1,
    Sent = 2,
    Delivered = 3,
    Failed = 4,
    Cancelled = 5
}

public enum MessagePriority
{
    Normal = 0,
    High = 1,
    Emergency = 2
}

public class OutboundMessage : AuditableEntity
{
    public Guid? CampusId { get; set; }
    public NotificationChannel Channel { get; set; }
    public MessagePriority Priority { get; set; }
    public string? TemplateCode { get; set; }
    public Guid? RecipientUserId { get; set; }
    public Guid? RecipientGuardianId { get; set; }
    public required string Recipient { get; set; }
    public string? Subject { get; set; }
    public required string Body { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.Queued;
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastError { get; set; }
    public string? RelatedEntityType { get; set; }
    public string? RelatedEntityId { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

/// <summary>Per-guardian / per-user channel preferences (CORE-08).</summary>
public class NotificationPreference : AuditableEntity
{
    public Guid? UserId { get; set; }
    public Guid? GuardianId { get; set; }
    public bool SmsEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public bool PushEnabled { get; set; } = true;
    public bool WhatsAppEnabled { get; set; }
    public string Language { get; set; } = "bn";
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }
}
