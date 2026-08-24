using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UPBazaar.Modules.Notifications.Contracts;

namespace UPBazaar.Modules.Notifications.Delivery;

/// <summary>
/// Development stand-in that writes the message to the log instead of sending it, so an OTP
/// flow can be exercised without a carrier account.
///
/// This deliberately logs content that the platform otherwise redacts, which is the whole
/// point of it and also why <see cref="NotificationsModuleExtensions"/> refuses to register it
/// outside Development. Registering it in production would put one-time codes in the log file.
/// </summary>
public sealed class DevSmsSender(ILogger<DevSmsSender> logger) : ISmsSender
{
    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogWarning(
            "DEV SMS to {Mobile}: {Body}",
            message.ToMobile,
            message.Body);

        return Task.CompletedTask;
    }
}

/// <summary>Development stand-in for email, with the same caveat as <see cref="DevSmsSender"/>.</summary>
public sealed class DevEmailSender(ILogger<DevEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogWarning(
            "DEV email to {Address} | {Subject} | {Body}",
            message.ToAddress,
            message.Subject,
            message.HtmlBody);

        return Task.CompletedTask;
    }
}

/// <summary>Refuses to deliver, so a misconfigured production host fails loudly.</summary>
public sealed class UnconfiguredSmsSender : ISmsSender
{
    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "No SMS provider is configured. Implement ISmsSender against a carrier and register "
            + "it in the Notifications module before running outside Development.");
}

/// <summary>Refuses to deliver, so a misconfigured production host fails loudly.</summary>
public sealed class UnconfiguredEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "No email provider is configured. Implement IEmailSender and register it in the "
            + "Notifications module before running outside Development.");
}
