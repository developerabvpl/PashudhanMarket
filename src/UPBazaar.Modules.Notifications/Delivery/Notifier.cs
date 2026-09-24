using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPBazaar.Infrastructure.Persistence;
using UPBazaar.Modules.Notifications.Contracts;
using UPBazaar.Modules.Notifications.Domain;
using UPBazaar.SharedKernel.Abstractions;

namespace UPBazaar.Modules.Notifications.Delivery;

/// <summary>
/// Sends one message and records it, once.
///
/// A message already recorded under the same key is not sent again, which makes every handler
/// safe against the outbox delivering an event twice. A provider failure is recorded, not thrown:
/// retrying the event would re-run every handler on it, and a text about an order is not worth
/// failing the refund or earning that the same event also drives. Failed messages stay in the
/// table for support to see.
/// </summary>
internal sealed partial class Notifier(
    UPBazaarDbContext dbContext,
    ISmsSender sms,
    IEmailSender email,
    IClock clock,
    ILogger<Notifier> logger)
{
    public Task SmsAsync(string key, string template, string? mobile, string body, CancellationToken cancellationToken) =>
        SendAsync(key, template, NotificationChannel.Sms, mobile, subject: null, body, cancellationToken);

    public Task EmailAsync(string key, string template, string? address, string subject, string htmlBody, CancellationToken cancellationToken) =>
        SendAsync(key, template, NotificationChannel.Email, address, subject, htmlBody, cancellationToken);

    private async Task SendAsync(
        string key,
        string template,
        NotificationChannel channel,
        string? recipient,
        string? subject,
        string body,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Set<NotificationMessage>().AnyAsync(m => m.Key == key, cancellationToken))
        {
            return;
        }

        var message = NotificationMessage.Create(key, template, channel, recipient, subject, body, clock.UtcNow);

        if (string.IsNullOrWhiteSpace(recipient))
        {
            message.MarkSkipped(channel == NotificationChannel.Sms ? "No mobile number." : "No email address.");
        }
        else
        {
            try
            {
                if (channel == NotificationChannel.Sms)
                {
                    await sms.SendAsync(new SmsMessage(recipient, body), cancellationToken);
                }
                else
                {
                    await email.SendAsync(new EmailMessage(recipient, subject!, body), cancellationToken);
                }

                message.MarkSent();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception, template, channel);
                message.MarkFailed(exception.Message);
            }
        }

        dbContext.Set<NotificationMessage>().Add(message);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(message).State = EntityState.Detached;

            // The same event handled twice at once: the other one recorded it first. Anything
            // else is a real failure.
            if (!await dbContext.Set<NotificationMessage>().AsNoTracking().AnyAsync(m => m.Key == key, cancellationToken))
            {
                throw;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send {Template} by {Channel}")]
    private static partial void LogFailed(ILogger logger, Exception exception, string template, NotificationChannel channel);
}
