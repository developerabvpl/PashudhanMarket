using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace UPBazaar.Infrastructure.ExternalServices.Notifications;

public sealed record SmsMessage(string ToNumber, string Body);

public sealed record EmailMessage(string ToAddress, string Subject, string HtmlBody);

public interface ISmsSender
{
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken);
}

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Records messages instead of sending them. Recipients are PII, so nothing is logged.</summary>
public sealed class FakeSmsSender(ILogger<FakeSmsSender> logger) : ISmsSender
{
    private readonly ConcurrentQueue<SmsMessage> _sent = new();

    public IReadOnlyCollection<SmsMessage> Sent => _sent;

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        logger.LogInformation("Sandbox SMS queued ({Length} chars)", message.Body.Length);
        return Task.CompletedTask;
    }
}

public sealed class FakeEmailSender(ILogger<FakeEmailSender> logger) : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        logger.LogInformation("Sandbox email queued with subject {Subject}", message.Subject);
        return Task.CompletedTask;
    }
}
