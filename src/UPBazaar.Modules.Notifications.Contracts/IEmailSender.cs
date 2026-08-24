namespace UPBazaar.Modules.Notifications.Contracts;

/// <summary>One email to one recipient.</summary>
/// <param name="ToAddress">Recipient address.</param>
/// <param name="Subject">Subject line, already localised.</param>
/// <param name="HtmlBody">Rendered HTML body.</param>
public sealed record EmailMessage(string ToAddress, string Subject, string HtmlBody);

/// <summary>Delivery boundary for email, for the same reason as <see cref="ISmsSender"/>.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
