using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Jobs;
using SchoolManagement.Infrastructure.Persistence;

namespace SchoolManagement.Infrastructure.Notifications;

/// <summary>Delivers queued messages, highest priority first, with exponential back-off and a retry cap.</summary>
public sealed class NotificationDispatchJob : IScheduledJob
{
    public const int MaxAttempts = 5;
    private const int BatchSize = 200;

    public string Name => "notification-dispatch";
    public TimeSpan Interval => TimeSpan.FromSeconds(10);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<TimeProvider>();
        var senders = services.GetServices<IMessageSender>().ToDictionary(s => s.Channel);
        var logger = services.GetRequiredService<ILogger<NotificationDispatchJob>>();
        var now = clock.GetUtcNow().UtcDateTime;

        var batch = await db.OutboundMessages
            .Where(m => m.Status == MessageStatus.Queued && m.NextAttemptAtUtc <= now)
            .OrderByDescending(m => m.Priority).ThenBy(m => m.NextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (batch.Count == 0) return;

        foreach (var message in batch)
        {
            message.Attempts++;
            SendResult result;
            try
            {
                result = senders.TryGetValue(message.Channel, out var sender)
                    ? await sender.SendAsync(message, ct)
                    : new SendResult(false, Error: $"No sender registered for {message.Channel}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Sending message {MessageId} failed", message.Id);
                result = new SendResult(false, Error: ex.Message);
            }

            if (result.Success)
            {
                message.Status = MessageStatus.Sent;
                message.SentAtUtc = clock.GetUtcNow().UtcDateTime;
                message.ProviderMessageId = result.ProviderMessageId;
                message.LastError = null;
            }
            else
            {
                message.LastError = result.Error;
                if (message.Attempts >= MaxAttempts)
                    message.Status = MessageStatus.Failed;
                else
                    message.NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(Math.Pow(2, message.Attempts));
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
