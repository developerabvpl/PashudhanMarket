namespace UPBazaar.Modules.Notifications.Contracts;

/// <summary>One SMS to one Indian mobile number.</summary>
/// <param name="ToMobile">Ten-digit national number, without country code.</param>
/// <param name="Body">Message text, already localised.</param>
/// <param name="TemplateId">DLT template id, required by Indian carriers for transactional SMS.</param>
public sealed record SmsMessage(string ToMobile, string Body, string? TemplateId = null);

/// <summary>
/// Delivery boundary for SMS. Owned by Notifications because it owns delivery; other modules
/// hold this interface rather than a provider client, so swapping carriers touches one place.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken);
}
