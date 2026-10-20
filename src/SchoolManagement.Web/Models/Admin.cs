using SchoolManagement.Domain.Approvals;
using SchoolManagement.Domain.Audit;
using SchoolManagement.Domain.Core;
using SchoolManagement.Domain.Notifications;

namespace SchoolManagement.Web.Models;

// Mirrors SchoolManagement.Api/Modules/Core: Approvals, Audit, Notifications, Backup, Search, Settings (and the campus list).
// Names are prefixed where the API's names are too generic to share one namespace with other modules.

public record AdminCampusOption(Guid Id, string Code, string Name, bool IsActive);

// Approvals
public record ApprovalWorkflowStepView(int Order, string Name, Guid ApproverRoleId, int? EscalateAfterHours, Guid? EscalateToRoleId);
public record ApprovalWorkflowView(Guid Id, string RequestType, Guid? CampusId, string Name, bool IsActive, IReadOnlyList<ApprovalWorkflowStepView> Steps);
public record ApprovalWorkflowStepRequest(string Name, Guid ApproverRoleId, int? EscalateAfterHours, Guid? EscalateToRoleId);
public record ApprovalWorkflowRequest(string RequestType, Guid? CampusId, string Name, bool IsActive, List<ApprovalWorkflowStepRequest> Steps);
public record ApprovalDecisionView(int StepOrder, Guid? DecidedBy, Guid? OnBehalfOf, ApprovalDecisionType Decision, string? Comment, DateTime DecidedAtUtc);
public record ApprovalRequestView(Guid Id, string RequestType, string EntityType, string EntityId, Guid CampusId, string? Summary, Guid RequestedBy,
    int CurrentStep, bool IsEscalated, ApprovalStatus Status, DateTime CreatedAtUtc, DateTime? CompletedAtUtc, IReadOnlyList<ApprovalDecisionView> Decisions);
public record ApprovalDecisionRequest(bool Approve, string? Comment);
public record ApprovalDelegationRequest(Guid ToUserId, DateTime StartsAtUtc, DateTime EndsAtUtc, string? RequestType);
public record ApprovalDelegationView(Guid Id, Guid FromUserId, Guid ToUserId, DateTime StartsAtUtc, DateTime EndsAtUtc, string? RequestType);

// Audit
public record AuditEventView(long Id, DateTime OccurredAtUtc, Guid? UserId, string? UserName, string? IpAddress, Guid? CampusId, AuditAction Action,
    string EntityType, string? EntityId, string? Description, string? OldValues, string? NewValues, bool IsSealed);
public record AuditVerificationResult(bool IsIntact, long CheckedEvents, long UnsealedEvents, IReadOnlyList<string> Problems);

// Notifications
public record MessageTemplateView(Guid Id, string Code, NotificationChannel Channel, string Language, string? Subject, string Body, int Version, bool IsActive);
public record MessageTemplateRequest(string Code, NotificationChannel Channel, string Language, string? Subject, string Body, bool IsActive = true);
public record TemplatePreviewRequest(string Code, NotificationChannel Channel, string Language, Dictionary<string, string?> Data);
public record TemplatePreviewResponse(string? Subject, string Body, int? SmsSegments);
public record OutboxMessageView(Guid Id, NotificationChannel Channel, MessagePriority Priority, string? TemplateCode, string Recipient, string? Subject, string Body,
    MessageStatus Status, int Attempts, DateTime CreatedAtUtc, DateTime? SentAtUtc, string? LastError);
public record SendMessageRequest(NotificationChannel Channel, List<string> Recipients, string? TemplateCode, Dictionary<string, string?>? Data, string? Language,
    string? Subject, string? Body, MessagePriority Priority = MessagePriority.Normal, Guid? CampusId = null);
public record SendMessageResponse(int Queued, IReadOnlyList<Guid> MessageIds);
public record NotificationInboxItem(Guid Id, string? Subject, string Body, DateTime CreatedAtUtc, DateTime? ReadAtUtc, string? RelatedEntityType, string? RelatedEntityId);
public record NotificationPreferenceView(bool SmsEnabled, bool EmailEnabled, bool PushEnabled, bool WhatsAppEnabled, string Language, TimeOnly? QuietHoursStart, TimeOnly? QuietHoursEnd);

// Backups (the API returns the BackupRecord entity as-is)
public record BackupRecordView(Guid Id, DateTime StartedAtUtc, DateTime? CompletedAtUtc, BackupStatus Status, string Trigger, string? FilePath, long? SizeBytes, string? Sha256, string? Error);
public record BackupVerification(bool Ok, string Message);

// Search
public record GlobalSearchHit(string Type, Guid Id, string Title, string? Subtitle, Guid? CampusId);
public record GlobalSearchResponse(string Query, IReadOnlyList<GlobalSearchHit> Results);

// Settings
public record SystemSettingView(string Key, Guid? CampusId, string? Value, string? Description);
public record SystemSettingRequest(string Key, Guid? CampusId, string? Value, string? Description);
public record NumberSequenceView(Guid Id, string Key, Guid? CampusId, Guid? AcademicYearId, string Prefix, string Pattern, long NextValue);
public record NumberSequenceRequest(string Prefix, string Pattern, long? NextValue);
public record LocalizationToday(DateOnly Date, string DhakaTime, string DateBangla, string Hijri, string AmountSample, string AmountSampleBangla);
