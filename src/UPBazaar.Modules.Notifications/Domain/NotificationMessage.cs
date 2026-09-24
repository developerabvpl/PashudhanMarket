using UPBazaar.SharedKernel.Primitives;

namespace UPBazaar.Modules.Notifications.Domain;

/// <summary>How a message reaches its recipient.</summary>
public enum NotificationChannel
{
    Sms = 0,
    Email = 1,
}

/// <summary>What became of a message.</summary>
public enum NotificationStatus
{
    /// <summary>Handed to the provider.</summary>
    Sent = 0,

    /// <summary>The provider refused it or could not be reached; see the error.</summary>
    Failed = 1,

    /// <summary>Not sent: there was nobody to send it to, such as a seller with no email address.</summary>
    Skipped = 2,
}

/// <summary>
/// One message the platform sent, or tried to: what was said, to whom, and how it went.
///
/// Kept for two reasons. Support can answer "did the buyer get the text?" from it. And its key -
/// the kind of message, the event that caused it, and the recipient - is unique, so an event the
/// outbox delivers twice cannot send the same message twice.
/// </summary>
public sealed class NotificationMessage : Entity
{
    public const int BodyMaxLength = 4000;

    private NotificationMessage()
    {
    }

    /// <summary>Kind, causing event and recipient, such as "order-confirmed:{eventId}".</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>The kind of message, such as "order-confirmed", for finding them by type.</summary>
    public string Template { get; private set; } = string.Empty;

    public NotificationChannel Channel { get; private set; }

    /// <summary>A mobile number or email address; empty when the message was skipped for want of one.</summary>
    public string Recipient { get; private set; } = string.Empty;

    public string? Subject { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public NotificationStatus Status { get; private set; }

    public string? Error { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static NotificationMessage Create(
        string key,
        string template,
        NotificationChannel channel,
        string? recipient,
        string? subject,
        string body,
        DateTime now) => new()
    {
        Key = key,
        Template = template,
        Channel = channel,
        Recipient = recipient ?? string.Empty,
        Subject = subject,
        Body = body.Length > BodyMaxLength ? body[..BodyMaxLength] : body,
        CreatedAtUtc = now,
    };

    public void MarkSent() => Status = NotificationStatus.Sent;

    public void MarkSkipped(string why)
    {
        Status = NotificationStatus.Skipped;
        Error = why;
    }

    public void MarkFailed(string error)
    {
        Status = NotificationStatus.Failed;
        Error = error.Length > 500 ? error[..500] : error;
    }
}
