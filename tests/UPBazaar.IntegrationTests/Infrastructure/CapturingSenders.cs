using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using UPBazaar.Modules.Notifications.Contracts;

namespace UPBazaar.IntegrationTests.Infrastructure;

/// <summary>
/// Captures SMS instead of sending it, so a test can read the code that was issued.
///
/// The production path never exposes the code — it is hashed the moment it is created — so the
/// only honest way to test the OTP flow end to end is to intercept it at the delivery
/// boundary, exactly where a real carrier would sit.
/// </summary>
public sealed class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<SmsMessage> _sent = new();

    public IReadOnlyCollection<SmsMessage> Sent => _sent;

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        _sent.Enqueue(message);

        return Task.CompletedTask;
    }

    /// <summary>The six-digit code from the most recent message to this number.</summary>
    public string? LatestCodeFor(string mobile)
    {
        var body = _sent.LastOrDefault(m => m.ToMobile == mobile)?.Body;

        if (body is null)
        {
            return null;
        }

        var match = Regex.Match(body, @"\b\d{6}\b", RegexOptions.None, TimeSpan.FromSeconds(1));

        return match.Success ? match.Value : null;
    }

    public void Clear() => _sent.Clear();
}

/// <summary>Captures email for the same reason.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        _sent.Enqueue(message);

        return Task.CompletedTask;
    }

    /// <summary>The reset token out of the most recent message to this address.</summary>
    public string? LatestResetTokenFor(string email)
    {
        var body = _sent.LastOrDefault(m => m.ToAddress == email)?.HtmlBody;

        if (body is null)
        {
            return null;
        }

        var match = Regex.Match(body, @"token=([^""&]+)", RegexOptions.None, TimeSpan.FromSeconds(1));

        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    public void Clear() => _sent.Clear();
}
