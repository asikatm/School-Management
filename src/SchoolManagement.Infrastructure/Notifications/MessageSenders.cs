using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolManagement.Domain.Notifications;

namespace SchoolManagement.Infrastructure.Notifications;

public record SendResult(bool Success, string? ProviderMessageId = null, string? Error = null);

/// <summary>Adapter for one delivery channel (spec 5.7: each integration isolated behind an interface).</summary>
public interface IMessageSender
{
    NotificationChannel Channel { get; }
    Task<SendResult> SendAsync(OutboundMessage message, CancellationToken ct);
}

public class SmsOptions
{
    /// <summary>HTTPS endpoint of the BTRC-licensed SMS provider. When empty, SMS is logged instead of sent.</summary>
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? SenderId { get; set; }
}

public class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@school.local";
    public string FromName { get; set; } = "School";
}

/// <summary>
/// Generic HTTP SMS adapter: posts api_key / senderid / number / message as a form, the shape most Bangladeshi
/// gateways accept. Bangla text is sent as UTF-8 Unicode.
/// </summary>
public sealed class HttpSmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<HttpSmsSender> logger) : IMessageSender
{
    public NotificationChannel Channel => NotificationChannel.Sms;

    public async Task<SendResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ApiUrl))
        {
            logger.LogInformation("[SMS not configured] To {Recipient}: {Body}", message.Recipient, message.Body);
            return new SendResult(true, "log-only");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["api_key"] = o.ApiKey ?? "",
            ["senderid"] = o.SenderId ?? "",
            ["number"] = message.Recipient,
            ["message"] = message.Body
        });
        using var response = await http.PostAsync(o.ApiUrl, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode
            ? new SendResult(true, body.Length > 128 ? body[..128] : body)
            : new SendResult(false, Error: $"HTTP {(int)response.StatusCode}: {body}");
    }
}

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IMessageSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<SendResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host))
        {
            logger.LogInformation("[E-mail not configured] To {Recipient}: {Subject}", message.Recipient, message.Subject);
            return new SendResult(true, "log-only");
        }

        using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = o.EnableSsl };
        if (!string.IsNullOrEmpty(o.UserName))
            client.Credentials = new NetworkCredential(o.UserName, o.Password);

        using var mail = new MailMessage(new MailAddress(o.FromAddress, o.FromName), new MailAddress(message.Recipient))
        {
            Subject = message.Subject ?? "",
            Body = message.Body,
            IsBodyHtml = message.Body.Contains('<'),
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        await client.SendMailAsync(mail, ct);
        return new SendResult(true);
    }
}

/// <summary>In-app messages are already stored in the outbox, which doubles as the user's inbox.</summary>
public sealed class InAppSender : IMessageSender
{
    public NotificationChannel Channel => NotificationChannel.InApp;
    public Task<SendResult> SendAsync(OutboundMessage message, CancellationToken ct) => Task.FromResult(new SendResult(true));
}

/// <summary>Placeholder for channels whose provider is chosen later (push via FCM/APNs in PRT, WhatsApp in P2).</summary>
public sealed class LoggingSender(NotificationChannel channel, ILogger logger) : IMessageSender
{
    public NotificationChannel Channel => channel;

    public Task<SendResult> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        logger.LogInformation("[{Channel} not configured] To {Recipient}: {Body}", channel, message.Recipient, message.Body);
        return Task.FromResult(new SendResult(true, "log-only"));
    }
}
